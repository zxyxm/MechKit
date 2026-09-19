using System;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace MechKit.Core
{
    /// <summary>与 SOLIDWORKS 文档打交道的零散帮助方法。</summary>
    internal static class SwUtils
    {
        public const int DocPart = 1;
        public const int DocAssembly = 2;
        public const int DocDrawing = 3;

        /// <summary>按扩展名推断 SOLIDWORKS 文档类型，未知返回 0。</summary>
        public static int DocTypeFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return 0;
            }

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".sldprt":
                    return DocPart;
                case ".sldasm":
                    return DocAssembly;
                case ".slddrw":
                    return DocDrawing;
                default:
                    return 0;
            }
        }

        public static bool IsSolidWorksFile(string path)
        {
            return DocTypeFromPath(path) != 0;
        }

        public static string DocTypeName(int docType)
        {
            switch (docType)
            {
                case DocPart:
                    return "零件";
                case DocAssembly:
                    return "装配体";
                case DocDrawing:
                    return "工程图";
                default:
                    return "未知";
            }
        }

        /// <summary>取当前激活文档，没有则返回 null。</summary>
        public static ModelDoc2 ActiveDoc(ISldWorks app)
        {
            if (app == null)
            {
                return null;
            }

            return app.ActiveDoc as ModelDoc2;
        }

        /// <summary>在已打开的文档中按路径查找，找不到返回 null。</summary>
        public static ModelDoc2 FindOpenDocument(ISldWorks app, string path)
        {
            if (app == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                return app.GetOpenDocumentByName(path) as ModelDoc2;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>取文档当前激活配置名，失败时返回空字符串（表示文档级属性）。</summary>
        public static string ActiveConfigurationName(ModelDoc2 doc)
        {
            try
            {
                if (doc == null)
                {
                    return string.Empty;
                }

                var config = doc.IGetActiveConfiguration();
                return config == null ? string.Empty : config.Name;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>BOM 文件名：装配体/零件文件名 + _BOM。</summary>
        public static string BomFileName(ModelDoc2 doc)
        {
            if (doc == null)
            {
                return "BOM";
            }

            try
            {
                var path = doc.GetPathName();
                if (!string.IsNullOrEmpty(path))
                {
                    return Path.GetFileNameWithoutExtension(path) + "-BOM";
                }

                var title = doc.GetTitle();
                return string.IsNullOrEmpty(title) ? "BOM" : title + "-BOM";
            }
            catch
            {
                return "BOM";
            }
        }

        /// <summary>文档材料名，未指定材料则返回空字符串。</summary>
        public static string MaterialName(ModelDoc2 doc)
        {
            try
            {
                if (doc == null)
                {
                    return string.Empty;
                }

                var material = doc.MaterialIdName;
                if (string.IsNullOrEmpty(material))
                {
                    return string.Empty;
                }

                // 形如 "solidworks materials.sldmat|普通碳钢"，只取材料名
                var index = material.LastIndexOf('|');
                return index >= 0 ? material.Substring(index + 1) : material;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
