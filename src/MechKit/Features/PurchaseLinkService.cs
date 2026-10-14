using System;
using System.IO;
using System.Linq;
using MechKit.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechKit.Features
{
    internal static class PurchaseLinkService
    {
        public const string PropertyName = "购买链接";

        public static string Validate(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0) return value;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("请输入完整的 http:// 或 https:// 购买网址；留空可清除链接。");
            return value;
        }

        public static void Save(ISldWorks app, string file, string value)
        {
            SaveProperty(app,file,PropertyName,value);
        }

        public static void SaveProperty(ISldWorks app, string file, string propertyName, string value)
        {
            if(propertyName!=PropertyName && propertyName!="安装说明" && propertyName!="备注")
                throw new ArgumentException("不支持的 BOM 文本属性。");
            value = propertyName==PropertyName ? Validate(value) : (value ?? "").Trim();
            if (app == null) throw new InvalidOperationException("SOLIDWORKS 未连接。");
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
                throw new InvalidOperationException("请先将虚拟件或未保存零件保存为外部文件。");
            var previous = app.IActiveDoc2;
            ModelDoc2 document = SwUtils.FindOpenDocument(app, file);
            bool opened = false;
            try
            {
                int errors=0, warnings=0;
                if (document == null)
                {
                    document = app.OpenDoc6(file, SwUtils.DocTypeFromPath(file),
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
                    opened = document != null && string.Equals(document.GetPathName(), file, StringComparison.OrdinalIgnoreCase);
                    if (document != null && !opened)
                    { document = null; throw new InvalidOperationException("SW 返回了其他同名文档，未写入链接。"); }
                }
                if (document == null) throw new InvalidOperationException("无法打开零件，错误码："+errors);
                if (!PropertyService.Write(document, PropertyService.DocumentLevelConfiguration,
                        new CustomProperty(propertyName, PropertyTypes.Text, value), true))
                    throw new InvalidOperationException(propertyName+"属性写入失败。");
                var property = PropertyService.Read(document, PropertyService.DocumentLevelConfiguration)
                    .FirstOrDefault(p => p.Name == propertyName);
                if (property == null || property.Value != value)
                    throw new InvalidOperationException(propertyName+"属性回读不一致。");
                if (!document.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) || errors != 0)
                    throw new InvalidOperationException(propertyName+"已写入内存，但文件保存失败，错误码："+errors);
                Log.Info("已保存"+propertyName+"："+file);
            }
            finally
            {
                if (opened && document != null) app.CloseDoc(document.GetTitle());
                if (previous != null && !Equals(app.IActiveDoc2,previous))
                {
                    int error=0;
                    app.ActivateDoc3(previous.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error);
                }
            }
        }
    }
}
