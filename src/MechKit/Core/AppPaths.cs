using System;
using System.IO;

namespace MechKit.Core
{
    /// <summary>插件在用户目录下的工作路径。</summary>
    internal static class AppPaths
    {
        public static string Root
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MechKit");
            }
        }

        public static string Logs { get { return Path.Combine(Root, "logs"); } }

        public static string Icons { get { return Path.Combine(Root, "icons"); } }

        public static string SettingsFile { get { return Path.Combine(Root, "settings.ini"); } }

        public static string Ensure(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return directory;
        }
    }
}
