using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MechKit.Core
{
    /// <summary>
    /// CommandManager 的图标必须以文件形式提供给 SOLIDWORKS，
    /// 因此把内嵌资源在连接时释放到用户目录。
    /// </summary>
    internal static class IconResources
    {
        // SOLIDWORKS 支持的图标尺寸（swImageSizeToUse_e）
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };

        private static bool _extracted;
        private static string[] _smallIcons = new string[0];
        private static string[] _mainIcons = new string[0];

        /// <summary>工具栏按钮图标条（每个尺寸一张，横向排列所有命令图标）。</summary>
        public static string[] SmallIcons { get { return _smallIcons; } }

        /// <summary>命令组主图标（每个尺寸一张）。</summary>
        public static string[] MainIcons { get { return _mainIcons; } }

        public static string TaskPaneIcon
        {
            get
            {
                var path = Path.Combine(AppPaths.Icons, "taskpane.png");
                return File.Exists(path) ? path : string.Empty;
            }
        }

        public static void Extract()
        {
            if (_extracted)
            {
                return;
            }

            AppPaths.Ensure(AppPaths.Icons);

            var assembly = Assembly.GetExecutingAssembly();
            var small = new List<string>();
            var main = new List<string>();

            foreach (var size in Sizes)
            {
                var smallPath = ExtractOne(assembly, "strip" + size + ".png", Path.Combine(AppPaths.Icons, "strip" + size + ".png"));
                if (smallPath != null)
                {
                    small.Add(smallPath);
                }

                var mainPath = ExtractOne(assembly, "main" + size + ".png", Path.Combine(AppPaths.Icons, "main" + size + ".png"));
                if (mainPath != null)
                {
                    main.Add(mainPath);
                }
            }

            ExtractOne(assembly, "taskpane.png", Path.Combine(AppPaths.Icons, "taskpane.png"));

            _smallIcons = small.ToArray();
            _mainIcons = main.ToArray();
            _extracted = true;
        }

        private static string ExtractOne(Assembly assembly, string fileName, string targetPath)
        {
            var resourceName = FindResource(assembly, fileName);
            if (resourceName == null)
            {
                Log.Warn("缺少内嵌图标资源：" + fileName);
                return null;
            }

            try
            {
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    // 图标是纯二进制资源，每次释放覆盖即可
                    using (var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        stream.CopyTo(file);
                    }
                }

                return targetPath;
            }
            catch (Exception ex)
            {
                Log.Error("释放图标失败：" + fileName, ex);
                return null;
            }
        }

        private static string FindResource(Assembly assembly, string fileName)
        {
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            return null;
        }
    }
}
