using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MechKit.Core;

namespace MechKit.Features
{
    internal sealed class BomImportField
    {
        public string Part { get; set; }
        public string Field { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
        public string Reason { get; set; }
        public bool Selected { get; set; }
        public bool CanApply { get; set; }
        public bool RequiresConfirmation { get; set; }
        internal string Key;
        internal List<PartListRow> Targets = new List<PartListRow>();
        internal Dictionary<PartListRow, string> Original = new Dictionary<PartListRow, string>();
        internal bool EmptyBefore;
    }

    internal sealed class BomImportPlan
    {
        public List<BomImportField> Fields { get; } = new List<BomImportField>();
        public List<string> Messages { get; } = new List<string>();
        public int Unmatched { get; set; }
        internal bool Applied;
        internal bool Linked;
    }

    internal static partial class BomImporter
    {
        private static string[] Tokens(string value)
        { return (value ?? "").Split(new[] { '；' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }

        private static string ModelKey(PartListRow row)
        { return string.IsNullOrWhiteSpace(row.FilePath) ? null : row.FilePath.Replace('/', '\\').ToUpperInvariant(); }

        public static BomImportPlan CreatePlan(string path, IList<PartListRow> scope, PartListOptions options, IList<PartListRow> allRows)
        { return CreatePlanCore(path, scope, options, allRows, false); }

        public static BomImportPlan CreatePlanCore(string path, IList<PartListRow> scope, PartListOptions options, IList<PartListRow> allRows, bool linked)
        {
            options = options ?? new PartListOptions();
            allRows = allRows ?? scope;
            var table = string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase) ? ReadWorkbook(path) :
                string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(path) :
                throw new InvalidDataException("请选择 XLSX 或 CSV 文件。");
            var aliases = BuildAliases(options);
            aliases[NormalizeHeader("关联快照")] = "snapshot";
            if (linked) foreach (var key in PartListService.ParseColumnOrder(null)) { aliases[NormalizeHeader(PartListService.ColumnHeader(options,key))] = key; aliases[NormalizeHeader(PartListService.ColumnHeader(new PartListOptions(),key))] = key; }
            aliases["模型路径"] = "filepath"; aliases["filepath"] = "filepath";
            Dictionary<string, int> columns = null;
            var header = -1;
            for (var i = 0; i < Math.Min(30, table.Count); i++)
            {
                var found = new Dictionary<string, int>();
                var duplicate = false;
                for (var c = 0; c < table[i].Cells.Count; c++)
                {
                    string key;
                    if (!aliases.TryGetValue(NormalizeHeader(table[i].Cells[c]), out key)) continue;
                    if (found.ContainsKey(key)) duplicate = true; else found[key] = c;
                }
                if (!(found.ContainsKey("fullname") || found.ContainsKey("name") || found.ContainsKey("filepath")) || !ValueKeys.Any(found.ContainsKey)) continue;
                if (duplicate) throw new InvalidDataException("表头重复，请保留每个字段的一列。");
                columns = found; header = i; break;
            }
            if (columns == null) throw new InvalidDataException("未找到零件名称和待导入字段。");
            var plan = new BomImportPlan { Linked = linked };
            var fieldIndex = new Dictionary<string, BomImportField>();
            foreach (var source in table.Skip(header + 1))
            {
                Func<string, string> get = key => columns.ContainsKey(key) && columns[key] < source.Cells.Count
                    ? (key == "purchaselink" && source.Links.ContainsKey(columns[key]) ? source.Links[columns[key]] : source.Cells[columns[key]]).Trim() : "";
                XElement snapshot = null;
                if (linked)
                {
                    if (get("snapshot").Length == 0 && get("fullname").Length == 0 && get("name").Length == 0) continue;
                    try { snapshot = BomExcelLink.ReadSnapshot(get("snapshot")); }
                    catch (Exception ex) { plan.Unmatched++; plan.Messages.Add("第 " + source.Number + " 行：" + ex.Message); continue; }
                }
                var full = Tokens(get("fullname"));
                var names = full.Length > 0 ? full : Tokens(get("name"));
                if (!linked && names.Length == 0 && get("filepath").Length == 0) continue; // blank and summary rows
                var locations = Tokens(get("location"));
                var matches = scope.Where(r => linked ? SameText((string)snapshot.Element("filepath"),r.FilePath) && SameText((string)snapshot.Element("configuration"),r.Configuration) : get("filepath").Length > 0
                    ? SameText(get("filepath").Replace('/', '\\'), r.FilePath == null ? "" : r.FilePath.Replace('/', '\\'))
                    : names.Any(n => SameName(n, full.Length > 0 ? r.FullName : r.Name))).ToList();
                if (get("configuration").Length > 0) matches = matches.Where(r => SameText(get("configuration"), r.Configuration)).ToList();
                if (!linked && locations.Length > 0) matches = matches.Where(r => locations.Any(l => SameText(l, r.Location))).ToList();
                var identities = matches.Select(r => ModelKey(r) + "\0" + r.Configuration).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var missingAlias = !linked && get("filepath").Length == 0 && names.Any(n => !matches.Any(r => SameName(n, full.Length > 0 ? r.FullName : r.Name)));
                var missingLocation = !linked && locations.Any(l => !matches.Any(r => SameText(l, r.Location)));
                if (matches.Count == 0 || matches.Any(r => ModelKey(r) == null) || identities != 1 || missingAlias || missingLocation)
                {
                    plan.Unmatched++;
                    plan.Messages.Add("第 " + source.Number + " 行未匹配：" + get("fullname") + "；零件/配置不唯一、未保存或合并位置/名称不完整。");
                    continue;
                }
                var model = ModelKey(matches[0]);
                var peers = allRows.Where(r => ModelKey(r) == model).ToList();
                if (peers.Count == 0) peers = matches;
                foreach (var key in (linked ? PartListService.ParseColumnOrder(null) : ValueKeys).Where(columns.ContainsKey))
                {
                    var value = get(key);
                    if (linked && value == ((string)snapshot.Element(key) ?? "")) continue;
                    if (!linked && value.Length == 0) continue; // explicit blank never deletes
                    var field = new BomImportField { Part = get("fullname").Length > 0 ? get("fullname") : get("name"),
                        Field = PartListService.ColumnHeader(options, key), Key = key, After = value,
                        Targets = peers, Reason = "第 " + source.Number + " 行；同步 " + peers.Count + " 个实例（含筛选隐藏行）" };
                    if (linked && !BomExcelLink.Editable(key))
                    {
                        field.Before = PartListService.ColumnValue(matches[0], 0, key);
                        field.Reason = "系统生成/只读字段发生改动，请人工核对；此项不写回，请在模型或命名工具中修改后重新导出。";
                        plan.Fields.Add(field); continue;
                    }
                    if (key == "quantity")
                    {
                        var total = matches.Sum(r => (long)r.Quantity);
                        decimal quantity;
                        var valid = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity) && quantity > 0 && decimal.Truncate(quantity) == quantity;
                        field.Before = total.ToString(CultureInfo.InvariantCulture);
                        field.Reason = valid && quantity == total ? "合计数量一致（只校验）" : "数量差异或格式错误，保留自动统计数量；其它字段可继续导入";
                        plan.Fields.Add(field); continue;
                    }
                    field.Original = peers.ToDictionary(r => r, r => PartListService.ColumnValue(r, 0, key) ?? "");
                    field.Before = string.Join("；", field.Original.Values.Distinct());
                    field.EmptyBefore = field.Original.Values.All(string.IsNullOrWhiteSpace);
                    field.CanApply = true;
                    try { if (key == "purchaselink") field.After = PurchaseLinkService.Validate(value); }
                    catch (ArgumentException ex) { field.CanApply = false; field.Reason = ex.Message; }
                    if (!linked && peers.Any(r => IsFieldDirty(r, key) && !SameText(PartListService.ColumnValue(r, 0, key), field.After)))
                    { field.CanApply = false; field.Reason = "此字段有未保存修改，保留当前值；请先处理后重新导入"; }
                    var indexKey = model + "\0" + key;
                    BomImportField previous;
                    if (fieldIndex.TryGetValue(indexKey, out previous))
                    {
                        if (!string.Equals(previous.After, field.After, StringComparison.Ordinal))
                        {
                            previous.CanApply = previous.Selected = false;
                            previous.Reason = "同一文件的多条导入记录为此字段提供不同值，未自动选择";
                            field.CanApply = false; field.Reason = previous.Reason;
                            plan.Fields.Add(field);
                        }
                        continue;
                    }
                    fieldIndex[indexKey] = field;
                    if (field.Original.Values.All(v => string.Equals(v, field.After, StringComparison.Ordinal)))
                    { field.CanApply = false; field.Reason = "内容一致，无需更新"; }
                    if (linked && field.Original.Values.Any(v => v != ((string)snapshot.Element(key) ?? "") && v != field.After))
                    { field.RequiresConfirmation = true; field.Reason = "插件与 Excel 均有修改，必须人工勾选确认覆盖。"; }
                    if (linked && value.Length == 0) { field.RequiresConfirmation = true; field.Reason = "Excel 清空了内容，必须人工勾选确认清除。"; }
                    field.Selected = field.CanApply && !field.RequiresConfirmation;
                    plan.Fields.Add(field);
                }
            }
            return plan;
        }

        private static bool IsFieldDirty(PartListRow row, string key)
        {
            var before = key == "material" ? row.OriginalMaterial : key == "process" ? row.OriginalProcess :
                key == "surface" ? row.OriginalSurfaceTreatment : key == "assemblynote" ? row.OriginalAssemblyNote :
                key == "remark" ? row.OriginalRemark : row.OriginalPurchaseLink;
            return !string.Equals(PartListService.ColumnValue(row, 0, key) ?? "", before ?? "", StringComparison.Ordinal);
        }

        public static BomImportResult ApplyPlan(BomImportPlan plan)
        {
            if (plan.Applied) throw new InvalidOperationException("此导入计划已经应用，请重新读取表格。");
            var result = new BomImportResult { Skipped = plan.Unmatched };
            result.Messages.AddRange(plan.Messages);
            var changed = new HashSet<PartListRow>();
            foreach (var field in plan.Fields)
            {
                if (!field.Selected || !field.CanApply) { result.Messages.Add(field.Part + " / " + field.Field + "：" + field.Reason); continue; }
                if (field.Original.Any(pair => (PartListService.ColumnValue(pair.Key, 0, field.Key) ?? "") != pair.Value))
                { result.Conflicts++; result.Messages.Add(field.Part + " / " + field.Field + "：预览后内容发生变化，此字段未更新"); continue; }
                if (plan.Linked)
                {
                    try { BomNoteStore.Save(field.Targets[0].FilePath, field.Key, field.After); }
                    catch (Exception ex) { result.Conflicts++; result.Messages.Add(field.Part + "：资产保存失败，未更新：" + ex.Message); continue; }
                }
                foreach (var row in field.Targets)
                {
                    switch (field.Key)
                    {
                        case "material": row.Material = field.After; break;
                        case "process": row.Process = field.After; break;
                        case "surface": row.SurfaceTreatment = field.After; break;
                        case "assemblynote": row.AssemblyNote = field.After; break;
                        case "remark": row.Remark = field.After; break;
                        case "unfinished": row.Unfinished = field.After; break;
                        case "purchaselink": row.PurchaseLink = field.After; break;
                    }
                    if (plan.Linked) row.MarkInlinePropertyClean(field.Key);
                    changed.Add(row);
                }
                result.Messages.Add(field.Part + " / " + field.Field + "：【" + field.Before + "】 → 【" + field.After + "】");
            }
            if (!plan.Linked) foreach (var row in changed) PartListService.ApplyAutomaticRemarks(row);
            result.Updated = changed.Count; plan.Applied = true;
            return result;
        }
    }
}
