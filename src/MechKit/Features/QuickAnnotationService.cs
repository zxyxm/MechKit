using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using MechKit.Core;

namespace MechKit.Features
{
    internal sealed class TolerancePreset
    {
        public string Name { get; private set; }
        public double LowerMeters { get; private set; }
        public double UpperMeters { get; private set; }
        public int Precision { get; private set; }
        public bool DiameterOnly { get; private set; }

        public TolerancePreset(string name, double lowerMm, double upperMm, int precision, bool diameterOnly = false)
        {
            Name = name;
            LowerMeters = lowerMm / 1000.0;
            UpperMeters = upperMm / 1000.0;
            Precision = precision;
            DiameterOnly = diameterOnly;
        }
    }

    internal static class QuickAnnotationService
    {
        public static readonly TolerancePreset[] Presets =
        {
            new TolerancePreset("+0.05 / 0 mm", 0, 0.05, 2),
            new TolerancePreset("0 / -0.05 mm", -0.05, 0, 2),
            new TolerancePreset("销钉孔：+0.012 / 0 mm", 0, 0.012, 3, true)
        };

        public static string Apply(ISldWorks swApp, TolerancePreset preset)
        {
            var doc = swApp == null ? null : swApp.IActiveDoc2;
            if (doc == null || doc.GetType() != SwUtils.DocDrawing)
                return "请先打开工程图并选择尺寸。";
            var selection = doc.SelectionManager as SelectionMgr;
            if (selection == null || selection.GetSelectedObjectCount2(-1) == 0)
                return "请先选择一个或多个工程图尺寸。";
            var applied = 0;
            var skipped = 0;
            var failed = 0;
            for (var i = 1; i <= selection.GetSelectedObjectCount2(-1); i++)
            {
                try
                {
                    var display = selection.GetSelectedObject6(i, -1) as DisplayDimension;
                    if (display == null) { skipped++; continue; }
                    var type = display.Type2;
                    if (type == (int)swDimensionType_e.swAngularDimension ||
                        type == (int)swDimensionType_e.swAngularOrdinateDimension ||
                        (preset.DiameterOnly && type != (int)swDimensionType_e.swDiameterDimension &&
                         type != (int)swDimensionType_e.swDiametricLinearDimension))
                    { skipped++; continue; }
                    var dimension = display.GetDimension2(0);
                    if (dimension == null || dimension.Tolerance == null) { skipped++; continue; }
                    var tolerance = dimension.Tolerance;
                    tolerance.Type = (int)swTolType_e.swTolBILAT;
                    if (!tolerance.SetValues2(preset.LowerMeters, preset.UpperMeters,
                        (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null))
                    { failed++; continue; }
                    var unchanged = (int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting;
                    if (display.SetPrecision3(unchanged, unchanged, preset.Precision, unchanged) != 0)
                    { failed++; continue; }
                    applied++;
                }
                catch (Exception ex) { failed++; Log.Error("设置快捷公差失败", ex); }
            }
            doc.GraphicsRedraw2();
            if (applied > 0) doc.SetSaveFlag();
            return string.Format("已标注 {0} 个；跳过 {1} 个；失败 {2} 个。", applied, skipped, failed);
        }
    }
}
