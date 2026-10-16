using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace MechKit.Features
{
    internal sealed class BomImportResult
    {
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<string> Messages { get; private set; } = new List<string>();
    }

    /// <summary>Import values into existing preview rows. Never opens or saves model documents.</summary>
    internal static class BomImporter
    {
        private static readonly string[] ValueKeys =
            { "material", "process", "surface", "quantity", "assemblynote", "remark", "purchaselink" };
        private static readonly string[] IdentityKeys = { "fullname", "name", "location", "configuration" };
        private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private sealed class TableRow
        {
            public int Number;
            public List<string> Cells;
            public Dictionary<int, string> Links = new Dictionary<int, string>();
        }

        private sealed class Update
        {
            public PartListRow Target;
            public int Number;
            public Dictionary<string, string> Values;
        }

        public static BomImportResult Import(string path, IList<PartListRow> targets, PartListOptions options)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var table = extension == ".xlsx" ? ReadWorkbook(path) :
                extension == ".csv" ? ReadCsv(path) :
                throw new InvalidDataException("请选择 .xlsx 或 .csv 文件；旧版 .xls 请先另存为 .xlsx。");
            var aliases = BuildAliases(options ?? new PartListOptions());
            Dictionary<string, int> columns = null;
            var headerIndex = -1;
            for (var i = 0; i < Math.Min(table.Count, 30); i++)
            {
                var found = new Dictionary<string, int>();
                var duplicates = new List<string>();
                for (var c = 0; c < table[i].Cells.Count; c++)
                {
                    string key;
                    if (!aliases.TryGetValue(NormalizeHeader(table[i].Cells[c]), out key)) continue;
                    if (found.ContainsKey(key)) duplicates.Add(table[i].Cells[c]);
                    else found.Add(key, c);
                }
                if ((!found.ContainsKey("fullname") && !found.ContainsKey("name")) ||
                    !ValueKeys.Any(found.ContainsKey)) continue;
                if (duplicates.Count > 0)
                    throw new InvalidDataException("表头重复，无法确定导入列：" + string.Join("、", duplicates));
                columns = found;
                headerIndex = i;
                break;
            }
            if (columns == null)
                throw new InvalidDataException("前 30 行未找到 BOM 表头。需要“完整名称”或“零件名称/标准件名称”，以及至少一列待导入参数。");

            var result = new BomImportResult();
            result.Messages.Add("识别列：" + string.Join("、", ValueKeys.Where(columns.ContainsKey)
                .Select(k => PartListService.ColumnHeader(new PartListOptions(), k))));
            var updates = new List<Update>();
            for (var i = headerIndex + 1; i < table.Count; i++)
            {
                var source = table[i];
                if (source.Cells.All(string.IsNullOrWhiteSpace)) continue;
                Func<string, string> get = key => columns.ContainsKey(key) && columns[key] < source.Cells.Count
                    ? (key == "purchaselink" && source.Links.ContainsKey(columns[key])
                        ? source.Links[columns[key]] : source.Cells[columns[key]]).Trim() : string.Empty;
                var fullName = get("fullname");
                var name = get("name");
                if (fullName.Length == 0 && name.Length == 0)
                {
                    Skip(result, source.Number, "名称为空");
                    continue;
                }
                // A supplied full name is authoritative; do not fall back to a different part with the same short name.
                var matches = targets.Where(r => fullName.Length > 0
                    ? SameName(fullName, r.FullName) : SameName(name, r.Name)).ToList();
                if (get("location").Length > 0)
                    matches = matches.Where(r => SameText(get("location"), r.Location)).ToList();
                if (get("configuration").Length > 0)
                    matches = matches.Where(r => SameText(get("configuration"), r.Configuration)).ToList();
                if (matches.Count != 1)
                {
                    Skip(result, source.Number, matches.Count == 0 ? "当前范围没有匹配零件：" + (fullName.Length > 0 ? fullName : name)
                        : "名称对应多行，请补充位置/配置或缩小 BOM 范围：" + (fullName.Length > 0 ? fullName : name));
                    continue;
                }
                var values = ValueKeys.Where(columns.ContainsKey).ToDictionary(k => k, get);
                // Empty cells are deliberately ignored, so a partial BOM cannot erase existing attributes.
                if (values.Values.All(string.IsNullOrWhiteSpace))
                {
                    Skip(result, source.Number, "参数均为空");
                    continue;
                }
                try
                {
                    string quantity;
                    if (values.TryGetValue("quantity", out quantity) && quantity.Length > 0)
                    {
                        decimal number;
                        if (!decimal.TryParse(quantity, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                            number <= 0 || number > int.MaxValue || decimal.Truncate(number) != number)
                            throw new InvalidDataException("数量必须是大于 0 的整数");
                        values["quantity"] = ((int)number).ToString(CultureInfo.InvariantCulture);
                    }
                    string link;
                    if (values.TryGetValue("purchaselink", out link) && link.Length > 0)
                        values["purchaselink"] = PurchaseLinkService.Validate(link);
                    updates.Add(new Update { Target = matches[0], Number = source.Number, Values = values });
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException)
                {
                    Skip(result, source.Number, ex.Message);
                }
            }
            // Resolve all rows before mutating anything: repeated source records must not silently overwrite one another.
            foreach (var group in updates.GroupBy(u => u.Target))
            {
                if (group.Count() != 1)
                {
                    foreach (var update in group) Skip(result, update.Number, "多条导入记录指向同一 BOM 行，已跳过");
                    continue;
                }
                var item = group.First();
                foreach (var value in item.Values.Where(v => v.Value.Length > 0))
                {
                    switch (value.Key)
                    {
                        case "material": item.Target.Material = value.Value; break;
                        case "process": item.Target.Process = value.Value; break;
                        case "surface": item.Target.SurfaceTreatment = value.Value; break;
                        case "quantity": item.Target.Quantity = int.Parse(value.Value, CultureInfo.InvariantCulture); break;
                        case "assemblynote": item.Target.AssemblyNote = value.Value; break;
                        case "remark": item.Target.Remark = value.Value; break;
                        case "purchaselink": item.Target.PurchaseLink = value.Value; break;
                    }
                }
                PartListService.ApplyAutomaticRemarks(item.Target);
                result.Updated++;
            }
            return result;
        }

        private static void Skip(BomImportResult result, int row, string reason)
        {
            result.Skipped++;
            result.Messages.Add("第 " + row + " 行：" + reason);
        }

        private static bool SameText(string a, string b)
        { return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase); }

        private static bool SameName(string a, string b)
        { return SameText(NormalizeName(a), NormalizeName(b)); }

        private static string NormalizeName(string value)
        {
            value = (value ?? "").Trim();
            foreach (var suffix in new[] { ".sldprt", ".sldasm" })
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return value.Substring(0, value.Length - suffix.Length);
            return value;
        }

        private static string NormalizeHeader(string value)
        { return new string((value ?? "").Where(c => !char.IsWhiteSpace(c) && c != '\uFEFF').ToArray()).Replace('／', '/').ToLowerInvariant(); }

        private static Dictionary<string, string> BuildAliases(PartListOptions options)
        {
            var aliases = new Dictionary<string, string>();
            Action<string, string> add = (header, key) =>
            {
                var normalized = NormalizeHeader(header);
                if (normalized.Length == 0) return;
                string existing;
                if (aliases.TryGetValue(normalized, out existing) && existing != key)
                    throw new InvalidDataException("BOM 表头设置存在冲突：" + header);
                aliases[normalized] = key;
            };
            foreach (var key in ValueKeys.Concat(IdentityKeys))
            {
                add(key, key);
                add(PartListService.ColumnHeader(new PartListOptions(), key), key);
                add(PartListService.ColumnHeader(options, key), key);
            }
            add("名称", "name"); add("零件名称", "name"); add("标准件名称", "name");
            add("文件名", "fullname"); add("三维名称", "fullname");
            add("材料", "material"); add("型号", "material"); add("材质", "material");
            add("工艺", "process"); add("渠道", "process");
            add("装配说明", "assemblynote"); add("配置", "configuration");
            return aliases;
        }

        private static List<TableRow> ReadCsv(string path)
        {
            string text;
            try
            {
                using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                    text = reader.ReadToEnd();
            }
            catch (DecoderFallbackException)
            { text = File.ReadAllText(path, Encoding.GetEncoding(936)); }
            // Excel may save CSV with semicolons, or a leading sep= directive.
            var delimiter = ',';
            var firstLineEnd = text.IndexOfAny(new[] { '\r', '\n' });
            var firstLine = firstLineEnd < 0 ? text : text.Substring(0, firstLineEnd);
            if (firstLine.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && firstLine.Length == 5)
            {
                delimiter = firstLine[4];
                text = firstLineEnd < 0 ? "" : text.Substring(firstLineEnd).TrimStart('\r', '\n');
            }
            else
            {
                var counts = new Dictionary<char, int> { { ',', 0 }, { ';', 0 }, { '\t', 0 } };
                var quoted = false;
                foreach (var ch in firstLine)
                {
                    if (ch == '"') quoted = !quoted;
                    else if (!quoted && counts.ContainsKey(ch)) counts[ch]++;
                }
                delimiter = counts.OrderByDescending(p => p.Value).First().Key;
            }
            var rows = new List<TableRow>();
            var cells = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var closedQuote = false;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inQuotes)
                {
                    if (ch != '"') field.Append(ch);
                    else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { inQuotes = false; closedQuote = true; }
                }
                else if (ch == delimiter || ch == '\r' || ch == '\n')
                {
                    cells.Add(field.ToString()); field.Clear(); closedQuote = false;
                    if (ch == delimiter) continue;
                    rows.Add(new TableRow { Number = rows.Count + 1, Cells = cells });
                    cells = new List<string>();
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else if (ch == '"' && field.Length == 0 && !closedQuote) inQuotes = true;
                else if (closedQuote || ch == '"') throw new InvalidDataException("CSV 引号格式错误，请重新保存 CSV。");
                else field.Append(ch);
            }
            if (inQuotes) throw new InvalidDataException("CSV 引号未闭合，请重新保存 CSV。");
            if (field.Length > 0 || cells.Count > 0 || closedQuote)
            {
                cells.Add(field.ToString());
                rows.Add(new TableRow { Number = rows.Count + 1, Cells = cells });
            }
            return rows;
        }

        private static XDocument ReadXml(ZipArchive zip, string path)
        {
            var entry = zip.GetEntry(path);
            if (entry == null) throw new InvalidDataException("Excel 缺少内容：" + path);
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return XDocument.Load(reader);
        }

        private static string ResolvePart(string parent, string target)
        { return Uri.UnescapeDataString(new Uri(new Uri("http://workbook/" + parent), target).AbsolutePath.TrimStart('/')); }

        private static List<TableRow> ReadWorkbook(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
            {
                var book = ReadXml(zip, "xl/workbook.xml");
                var relationships = ReadXml(zip, "xl/_rels/workbook.xml.rels").Root.Elements().ToList();
                var first = book.Descendants(Sheet + "sheet").FirstOrDefault();
                if (first == null) throw new InvalidDataException("Excel 中没有工作表。");
                var relation = relationships.FirstOrDefault(r => (string)r.Attribute("Id") == (string)first.Attribute(Rel + "id"));
                if (relation == null || (string)relation.Attribute("TargetMode") == "External")
                    throw new InvalidDataException("无法读取首个工作表。");
                var sheetPath = ResolvePart("xl/workbook.xml", (string)relation.Attribute("Target"));
                var sharedRelation = relationships.FirstOrDefault(r => ((string)r.Attribute("Type") ?? "").EndsWith("/sharedStrings"));
                var strings = sharedRelation == null ? new List<string>() :
                    ReadXml(zip, ResolvePart("xl/workbook.xml", (string)sharedRelation.Attribute("Target")))
                    .Descendants(Sheet + "si").Select(e => string.Concat(e.Descendants(Sheet + "t").Select(t => t.Value))).ToList();
                var worksheet = ReadXml(zip, sheetPath);
                var links = new Dictionary<string, string>();
                var relPath = sheetPath.Substring(0, sheetPath.LastIndexOf('/') + 1) + "_rels/" + Path.GetFileName(sheetPath) + ".rels";
                if (zip.GetEntry(relPath) != null)
                {
                    var sheetRels = ReadXml(zip, relPath).Root.Elements().ToList();
                    foreach (var link in worksheet.Descendants(Sheet + "hyperlink"))
                    {
                        var target = sheetRels.FirstOrDefault(r => (string)r.Attribute("Id") == (string)link.Attribute(Rel + "id"));
                        if (target != null && (string)target.Attribute("TargetMode") == "External")
                            links[(string)link.Attribute("ref")] = (string)target.Attribute("Target") ?? "";
                    }
                }
                var rows = new List<TableRow>();
                foreach (var row in worksheet.Descendants(Sheet + "sheetData").Elements(Sheet + "row"))
                {
                    var cells = new List<string>();
                    var tableRow = new TableRow { Number = (int?)row.Attribute("r") ?? rows.Count + 1, Cells = cells };
                    foreach (var cell in row.Elements(Sheet + "c"))
                    {
                        var reference = (string)cell.Attribute("r") ?? "";
                        var column = 0;
                        foreach (var ch in reference.TakeWhile(char.IsLetter)) column = checked(column * 26 + char.ToUpperInvariant(ch) - 'A' + 1);
                        if (column == 0) column = cells.Count + 1;
                        if (column > 16384) throw new InvalidDataException("Excel 列号超出范围。");
                        while (cells.Count < column) cells.Add("");
                        var type = (string)cell.Attribute("t");
                        var value = type == "inlineStr" ? string.Concat(cell.Descendants(Sheet + "t").Select(t => t.Value)) : (string)cell.Element(Sheet + "v") ?? "";
                        if (type == "s")
                        {
                            int index;
                            if (!int.TryParse(value, out index) || index < 0 || index >= strings.Count)
                                throw new InvalidDataException("Excel 共享文本索引无效：" + reference);
                            value = strings[index];
                        }
                        string url;
                        if (links.TryGetValue(reference, out url)) tableRow.Links[column - 1] = url;
                        if (cell.Element(Sheet + "f") != null && cell.Element(Sheet + "v") == null && value.Length == 0)
                            throw new InvalidDataException("单元格 " + reference + " 的公式没有缓存值，请在 Excel 中计算并保存后重试。");
                        cells[column - 1] = value;
                    }
                    rows.Add(tableRow);
                }
                return rows;
            }
        }
    }
}
