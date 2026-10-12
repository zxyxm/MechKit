using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using MechKit.Core;
using SolidWorks.Interop.sldworks;

namespace MechKit.Features
{
    /// <summary>Self-contained XLSX with embedded previews; does not require Excel.</summary>
    internal static class BomExcelExporter
    {
        private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace Content = "http://schemas.openxmlformats.org/package/2006/content-types";
        private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        private static readonly XNamespace Art = "http://schemas.openxmlformats.org/drawingml/2006/main";

        public static string Export(ISldWorks app, IList<PartListRow> rows, string folder, string title,
            PartListOptions options)
        {
            return ExportWithPreviews(rows, folder, title, options, row => ReadPreview(app, row));
        }

        internal static string ExportWithPreviews(IList<PartListRow> rows, string folder, string title,
            PartListOptions options, Func<PartListRow, byte[]> preview)
        {
            options = options ?? new PartListOptions();
            AppPaths.Ensure(folder);
            var path = Path.Combine(folder, string.Format("{0}-{1:yyyyMMdd-HHmmss-fff}.xlsx",
                string.IsNullOrEmpty(title) ? "明细汇总" : title, DateTime.Now));
            var columns = PartListService.ParseColumnOrder(options.ColumnOrder);
            var data = new XElement(Sheet + "sheetData");
            var header = new XElement(Sheet + "row", new XAttribute("r", 1));
            header.Add(Cell("A1", "零件缩略图"));
            for (var c = 0; c < columns.Length; c++)
                header.Add(Cell(Column(c + 2) + "1", PartListService.ColumnHeader(options, columns[c])));
            data.Add(header);
            var drawing = new XElement(Drawing + "wsDr");
            var drawingRels = new XElement(Package + "Relationships");
            var cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var file = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var imageId = 0;
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var key = (row.FilePath ?? "") + "\0" + (row.Configuration ?? "");
                    byte[] png;
                    if (!cache.TryGetValue(key, out png))
                    {
                        png = preview(row);
                        cache[key] = png;
                    }
                    var number = i + 2;
                    var cells = new XElement(Sheet + "row", new XAttribute("r", number),
                        new XAttribute("ht", 60), new XAttribute("customHeight", 1));
                    cells.Add(Cell("A" + number, png == null ? "无缩略图" : ""));
                    for (var c = 0; c < columns.Length; c++)
                        cells.Add(Cell(Column(c + 2) + number,
                            PartListService.ColumnValue(row, i + 1, columns[c]), columns[c] == "quantity" || columns[c] == "sequence"));
                    data.Add(cells);
                    if (png == null) continue;
                    imageId++;
                    using (var stream = zip.CreateEntry("xl/media/image" + imageId + ".png").Open())
                        stream.Write(png, 0, png.Length);
                    drawingRels.Add(Relationship("rId" + imageId, "image", "../media/image" + imageId + ".png"));
                    drawing.Add(Picture(imageId, i + 1));
                }
                data.Add(new XElement(Sheet + "row", new XAttribute("r", rows.Count + 3),
                    Cell("A" + (rows.Count + 3), PartListService.BuildSummary(rows))));
                var worksheet = new XElement(Sheet + "worksheet",
                    new XElement(Sheet + "sheetViews", new XElement(Sheet + "sheetView", new XAttribute("workbookViewId", 0),
                        new XElement(Sheet + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"),
                            new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                    new XElement(Sheet + "cols",
                        new XElement(Sheet + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 16), new XAttribute("customWidth", 1)),
                        new XElement(Sheet + "col", new XAttribute("min", 2), new XAttribute("max", columns.Length + 1), new XAttribute("width", 22), new XAttribute("customWidth", 1))), data,
                    new XElement(Sheet + "autoFilter", new XAttribute("ref", "A1:" + Column(columns.Length + 1) + (rows.Count + 1))));
                var types = new XElement(Content + "Types",
                    new XElement(Content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(Content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    Override("/xl/workbook.xml", "spreadsheetml.sheet.main"), Override("/xl/worksheets/sheet1.xml", "spreadsheetml.worksheet"));
                if (imageId > 0)
                {
                    types.Add(new XElement(Content + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
                        Override("/xl/drawings/drawing1.xml", "drawing"));
                    worksheet.Add(new XElement(Sheet + "drawing", new XAttribute(Rel + "id", "rId1")));
                    Write(zip, "xl/worksheets/_rels/sheet1.xml.rels", new XElement(Package + "Relationships", Relationship("rId1", "drawing", "../drawings/drawing1.xml")));
                    Write(zip, "xl/drawings/drawing1.xml", drawing);
                    Write(zip, "xl/drawings/_rels/drawing1.xml.rels", drawingRels);
                }
                Write(zip, "[Content_Types].xml", types);
                Write(zip, "_rels/.rels", new XElement(Package + "Relationships", Relationship("rId1", "officeDocument", "xl/workbook.xml")));
                Write(zip, "xl/workbook.xml", new XElement(Sheet + "workbook", new XElement(Sheet + "sheets",
                    new XElement(Sheet + "sheet", new XAttribute("name", "BOM"), new XAttribute("sheetId", 1), new XAttribute(Rel + "id", "rId1")))));
                Write(zip, "xl/_rels/workbook.xml.rels", new XElement(Package + "Relationships", Relationship("rId1", "worksheet", "worksheets/sheet1.xml")));
                Write(zip, "xl/worksheets/sheet1.xml", worksheet);
            }
            return path;
        }

        private static byte[] ReadPreview(ISldWorks app, PartListRow row)
        {
            if (app == null || string.IsNullOrEmpty(row.FilePath) || !File.Exists(row.FilePath)) return null;
            var temp = Path.Combine(Path.GetTempPath(), "MechKit-preview-" + Guid.NewGuid().ToString("N") + ".bmp");
            try
            {
                if (!app.GetPreviewBitmapFile(row.FilePath, row.Configuration ?? "", temp) || !File.Exists(temp)) return null;
                using (var source = Image.FromFile(temp))
                using (var target = new Bitmap(104, 72))
                using (var graphics = Graphics.FromImage(target))
                using (var stream = new MemoryStream())
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    var scale = Math.Min(104f / source.Width, 72f / source.Height);
                    var width = source.Width * scale;
                    var height = source.Height * scale;
                    graphics.DrawImage(source, (104 - width) / 2, (72 - height) / 2, width, height);
                    target.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取 BOM 缩略图失败：" + row.FilePath + "：" + ex.Message);
                return null;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) { Log.Warn("清理 BOM 缩略图失败：" + ex.Message); }
            }
        }

        private static XElement Picture(int id, int row)
        {
            return new XElement(Drawing + "oneCellAnchor",
                new XElement(Drawing + "from", new XElement(Drawing + "col", 0), new XElement(Drawing + "colOff", 38100),
                    new XElement(Drawing + "row", row), new XElement(Drawing + "rowOff", 38100)),
                new XElement(Drawing + "ext", new XAttribute("cx", 990600), new XAttribute("cy", 685800)),
                new XElement(Drawing + "pic",
                    new XElement(Drawing + "nvPicPr", new XElement(Drawing + "cNvPr", new XAttribute("id", id), new XAttribute("name", "零件缩略图 " + id)),
                        new XElement(Drawing + "cNvPicPr", new XElement(Art + "picLocks", new XAttribute("noChangeAspect", 1)))),
                    new XElement(Drawing + "blipFill", new XElement(Art + "blip", new XAttribute(Rel + "embed", "rId" + id)),
                        new XElement(Art + "stretch", new XElement(Art + "fillRect"))),
                    new XElement(Drawing + "spPr", new XElement(Art + "prstGeom", new XAttribute("prst", "rect"), new XElement(Art + "avLst")))),
                new XElement(Drawing + "clientData"));
        }

        private static XElement Cell(string address, string value, bool numeric = false)
        {
            return new XElement(Sheet + "c", new XAttribute("r", address), new XAttribute("t", numeric ? "n" : "inlineStr"),
                numeric ? new XElement(Sheet + "v", value) : new XElement(Sheet + "is",
                    new XElement(Sheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? "")));
        }

        private static string Column(int index)
        {
            var value = "";
            while (index > 0) { index--; value = (char)('A' + index % 26) + value; index /= 26; }
            return value;
        }

        private static XElement Relationship(string id, string type, string target)
        {
            return new XElement(Package + "Relationship", new XAttribute("Id", id), new XAttribute("Type", Rel.NamespaceName + "/" + type), new XAttribute("Target", target));
        }

        private static XElement Override(string name, string type)
        {
            return new XElement(Content + "Override", new XAttribute("PartName", name),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument." + type + "+xml"));
        }

        private static void Write(ZipArchive zip, string name, XElement element)
        {
            using (var stream = zip.CreateEntry(name).Open()) new XDocument(element).Save(stream);
        }
    }
}
