using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using MechKit.Core;

namespace MechKit.Features
{
    internal sealed class TolerancePreset
    {
        public string Name { get; private set; }
        public string Note { get; private set; }
        public double LowerMeters { get; private set; }
        public double UpperMeters { get; private set; }
        public int Precision { get; private set; }
        public int Type { get; private set; }
        public string HoleFit { get; private set; } = "";
        public string ShaftFit { get; private set; } = "";

        public static TolerancePreset Parse(string text)
        {
            var fields = (text ?? "").Trim().Replace("+/-", "±").Replace('／', '/').Split(new[] { '/' }, 3);
            var value = Regex.Replace(fields[0], @"\s+", "").Replace('−', '-');
            var preset = new TolerancePreset();
            if (value == "清除公差")
            { preset.Type = (int)swTolType_e.swTolNONE; preset.Name = value; return preset; }
            if (Regex.IsMatch(value, @"^[A-Za-z]{1,2}(?:[1-9]|1[0-8])$"))
            {
                if (fields.Length > 2) throw new FormatException("配合公差格式：H7/备注 或 h7/备注。");
                preset.Type = (int)swTolType_e.swTolFIT;
                if (char.IsUpper(value[0])) preset.HoleFit = value;
                else preset.ShaftFit = value;
                preset.Note = fields.Length == 2 ? fields[1].Trim() : "";
                preset.Name = value;
                return preset;
            }
            string upperText, lowerText;
            if (value.StartsWith("±") || value.StartsWith("+/-"))
            {
                upperText = value.Substring(value[0] == '±' ? 1 : 3);
                var magnitude = Number(upperText);
                if (magnitude < 0) throw new FormatException("对称公差须为非负数。");
                preset.UpperMeters = magnitude / 1000;
                preset.LowerMeters = -preset.UpperMeters;
                preset.Type = (int)swTolType_e.swTolSYMMETRIC;
                preset.Note = fields.Length == 2 ? fields[1].Trim() : "";
                if (fields.Length > 2) throw new FormatException("对称公差格式：±0.02/备注。");
                preset.Name = "±" + upperText;
                lowerText = upperText;
            }
            else
            {
                // The editor uses upper/lower, while SOLIDWORKS takes minimum/maximum.
                if (fields.Length < 2) throw new FormatException("请输入 上公差/下公差/备注，例如 0.03/0/销钉，或 ±0.02、H7。");
                upperText = Regex.Replace(fields[0], @"\s+", "").Replace('−', '-');
                lowerText = Regex.Replace(fields[1], @"\s+", "").Replace('−', '-');
                var upper = Number(upperText);
                var lower = Number(lowerText);
                if (upper < lower) throw new FormatException("上公差不能小于下公差。");
                preset.UpperMeters = upper / 1000;
                preset.LowerMeters = lower / 1000;
                preset.Type = (int)swTolType_e.swTolBILAT;
                preset.Note = fields.Length == 3 ? fields[2].Trim() : "";
                preset.Name = upperText + "\r\n" + lowerText;
            }
            preset.Precision = Math.Max(DecimalPlaces(upperText), DecimalPlaces(lowerText));
            if (preset.Precision > 8) throw new FormatException("公差最多支持 8 位小数。");
            return preset;
        }

        private static double Number(string text)
        {
            double value;
            if (!Regex.IsMatch(text, @"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$") ||
                !double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out value) || double.IsInfinity(value))
                throw new FormatException("公差必须是数字，使用小数点；单位为 mm。");
            return value;
        }
        private static int DecimalPlaces(string text) { var dot = text.IndexOf('.'); return dot < 0 ? 0 : text.Length - dot - 1; }
        public string Label { get { return Name + (string.IsNullOrEmpty(Note) ? "" : "\r\n" + Note); } }
    }

    internal sealed class HoleParameter
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public override string ToString() { return Label; }
    }

    internal static class QuickAnnotationService
    {
        public const int CellCount = 35;
        public static string[] DefaultCells(int mode)
        {
            var cells = new string[CellCount];
            if (mode == 0)
            {
                var values = new[] { "0.02/0", "±0.02", "±0.03", "±0.04", "±0.05", "-0.01/-0.03", "±0.1", "0.01/-0.02/轴用", "-0.02/-0.05/孔用", "±1", "0.05/0/正公差", "0/-0.05/负公差", "0.012/0/销钉孔", "0.03/0/销钉" };
                Array.Copy(values, cells, values.Length);
                Array.Copy(new[] { "H7", "H8", "H9", "H11", "F8", "g6", "h7", "h9", "h11", "" }, 0, cells, 25, 10);
            }
            else if (mode == 1) Array.Copy(new[] { "2×", "4×", "6×", "8×", "配作" }, cells, 5);
            else Array.Copy(new[] { "完全贯穿", "配钻", "铰孔", "深", "参考" }, cells, 5);
            return cells;
        }
        public static string EncodeCells(string[] cells)
        { return string.Join(".", cells.Select(v => Convert.ToBase64String(Encoding.UTF8.GetBytes(v ?? "")))); }
        public static string[] DecodeCells(string encoded, int mode)
        {
            if (string.IsNullOrEmpty(encoded)) return DefaultCells(mode);
            var cells = new string[CellCount];
            var values = encoded.Split('.');
            for (var i = 0; i < Math.Min(values.Length, cells.Length); i++)
                try { cells[i] = Encoding.UTF8.GetString(Convert.FromBase64String(values[i])); }
                catch (FormatException) { cells[i] = ""; }
            return cells;
        }

        public static List<HoleParameter> GetHoleParameters(ISldWorks app)
        {
            var result = new List<HoleParameter>();
            var doc = app == null ? null : app.IActiveDoc2;
            if (doc == null || doc.GetType() != SwUtils.DocDrawing) return result;
            var selection = doc.SelectionManager as SelectionMgr;
            if (selection == null) return result;
            for (var i = 1; i <= selection.GetSelectedObjectCount2(-1); i++)
            {
                var display = selection.GetSelectedObject6(i, -1) as DisplayDimension;
                if (display == null || !display.IsHoleCallout()) continue;
                var variables = display.GetHoleCalloutVariables() as object[];
                if (variables == null) continue;
                foreach (var item in variables)
                {
                    var variable = item as CalloutVariable;
                    if (variable == null || !(item is CalloutLengthVariable)) continue;
                    if (result.Any(v => v.Name == variable.VariableName)) continue;
                    result.Add(new HoleParameter { Name = variable.VariableName, Label = variable.UserReadableVariableName + " [" + variable.VariableName + "]" });
                }
            }
            return result;
        }

        internal static string ResolveHoleParameter(IList<HoleParameter> parameters, string selected)
        {
            if (parameters == null || parameters.Count == 0) return null;
            if (!string.IsNullOrEmpty(selected) && parameters.Any(p => p.Name == selected)) return selected;
            return parameters.Count == 1 ? parameters[0].Name : null;
        }

        // mode 0 = tolerance, 1 = prefix, 2 = suffix. Notes label presets only.
        public static string Apply(ISldWorks swApp, string text, int mode, string holeParameter = null)
        {
            var preset = mode == 0 ? TolerancePreset.Parse(text) : null;
            var doc = swApp == null ? null : swApp.IActiveDoc2;
            if (doc == null || doc.GetType() != SwUtils.DocDrawing) return "请先打开工程图并选择尺寸。";
            var selection = doc.SelectionManager as SelectionMgr;
            if (selection == null || selection.GetSelectedObjectCount2(-1) == 0) return "请先选择一个或多个工程图尺寸。";
            var applied = 0; var skipped = 0; var failed = 0; var holeNeedsParameter = false;
            for (var i = 1; i <= selection.GetSelectedObjectCount2(-1); i++)
            {
                try
                {
                    var display = selection.GetSelectedObject6(i, -1) as DisplayDimension;
                    if (display == null) { skipped++; continue; }
                    if (display.IsHoleCallout())
                    {
                        if (mode != 0) { skipped++; continue; } // SetText does not support hole callouts.
                        if (string.IsNullOrEmpty(holeParameter)) { skipped++; holeNeedsParameter = true; continue; }
                        var variables = display.GetHoleCalloutVariables() as object[];
                        var item = variables == null ? null : variables.FirstOrDefault(v => (v as CalloutVariable)?.VariableName == holeParameter);
                        var variable = item as CalloutVariable;
                        var length = item as CalloutLengthVariable;
                        if (variable == null || length == null) { skipped++; continue; }
                        variable.ToleranceType = preset.Type;
                        if (preset.Type == (int)swTolType_e.swTolFIT)
                        { variable.FitType = (int)swFitType_e.swFitUSER; variable.HoleFit = preset.HoleFit; variable.ShaftFit = preset.ShaftFit; }
                        else if (preset.Type != (int)swTolType_e.swTolNONE)
                        { variable.ToleranceMin = preset.LowerMeters; variable.ToleranceMax = preset.UpperMeters; length.TolerancePrecision = preset.Precision; }
                        applied++;
                        continue;
                    }
                    if (mode != 0)
                    { display.SetText((int)(mode == 1 ? swDimensionTextParts_e.swDimensionTextPrefix : swDimensionTextParts_e.swDimensionTextSuffix), text ?? ""); applied++; continue; }
                    var type = display.Type2;
                    if (type == (int)swDimensionType_e.swAngularDimension || type == (int)swDimensionType_e.swAngularOrdinateDimension)
                    { skipped++; continue; }
                    var dimension = display.GetDimension2(0);
                    if (dimension == null || dimension.Tolerance == null) { skipped++; continue; }
                    var tolerance = dimension.Tolerance;
                    tolerance.Type = preset.Type;
                    var ok = true;
                    if (preset.Type == (int)swTolType_e.swTolFIT)
                        { tolerance.FitType = (int)swFitType_e.swFitUSER; ok = tolerance.SetFitValues(preset.HoleFit, preset.ShaftFit); }
                    else if (preset.Type != (int)swTolType_e.swTolNONE)
                    {
                        // Drawing reference dimensions have no model configuration. In SW 2024
                        // SetValues2 returns false for them; SetValues writes the displayed tolerance.
                        ok = tolerance.SetValues(preset.LowerMeters, preset.UpperMeters);
                        ok = ok && tolerance.Type == preset.Type &&
                            Math.Abs(tolerance.GetMinValue() - preset.LowerMeters) < 1e-12 &&
                            Math.Abs(tolerance.GetMaxValue() - preset.UpperMeters) < 1e-12;
                        var unchanged = (int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting;
                        if (ok)
                        {
                            var precisionResult = display.SetPrecision3(unchanged, unchanged, preset.Precision, unchanged);
                            ok = precisionResult >= 0;
                            if (precisionResult != 0) Log.Warn("设置公差精度返回：" + precisionResult);
                        }
                    }
                    if (!ok) Log.Warn("公差写入未通过验证：" + dimension.FullName + "；预设=" + text + "；实际类型=" + tolerance.Type);
                    if (ok) applied++; else failed++;
                }
                catch (Exception ex) { failed++; Log.Error("设置快捷公差失败", ex); }
            }
            doc.GraphicsRedraw2();
            // An API failure may occur after the tolerance type changed.
            if (applied > 0 || failed > 0) doc.SetSaveFlag();
            return string.Format("已写入 {0} 个；跳过 {1} 个；失败 {2} 个。", applied, skipped, failed) +
                (holeNeedsParameter ? "孔标注请先读取并选择参数。" : "");
        }
    }
}
