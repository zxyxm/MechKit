using System;
using System.IO;
using Microsoft.Win32;

namespace MechKit.Core
{
    /// <summary>定位 SOLIDWORKS 的常用目录（焊件库、模板库、宏、Toolbox）。</summary>
    internal static class SwFolders
    {
        /// <summary>SOLIDWORKS 安装目录（读注册表 Setup\SolidWorks Folder）。</summary>
        public static string InstallFolder
        {
            get
            {
                try
                {
                    using (var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SolidWorks"))
                    {
                        if (root == null)
                        {
                            return string.Empty;
                        }

                        var versions = root.GetSubKeyNames();
                        Array.Sort(versions, StringComparer.OrdinalIgnoreCase);
                        Array.Reverse(versions);

                        foreach (var version in versions)
                        {
                            if (!version.StartsWith("SOLIDWORKS ", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            using (var setup = root.OpenSubKey(version + @"\Setup"))
                            {
                                if (setup == null)
                                {
                                    continue;
                                }

                                var folder = setup.GetValue("SolidWorks Folder") as string;
                                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                                {
                                    return folder.TrimEnd('\\');
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("读取 SOLIDWORKS 安装目录失败：" + ex.Message);
                }

                return string.Empty;
            }
        }

        public static string VersionFolder
        {
            get
            {
                try
                {
                    using (var root = Registry.CurrentUser.OpenSubKey(@"Software\SolidWorks"))
                    {
                        if (root != null)
                        {
                            var names = root.GetSubKeyNames();
                            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                            Array.Reverse(names);
                            foreach (var name in names)
                            {
                                if (name.StartsWith("SOLIDWORKS ", StringComparison.OrdinalIgnoreCase))
                                {
                                    return name;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略
                }

                return "SOLIDWORKS";
            }
        }

        public static string WeldmentProfiles()
        {
            var install = InstallFolder;
            return FirstExisting(
                Combine(install, @"lang\chinese-simplified\weldment profiles"),
                Combine(install, @"lang\english\weldment profiles"),
                Combine(install, @"data\weldment profiles"),
                Combine(install, @"lang\chinese-simplified\weldment Profiles"));
        }

        public static string DrawingTemplates()
        {
            var version = VersionFolder;
            return FirstExisting(
                Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"SOLIDWORKS\" + version + @"\templates"),
                Combine(InstallFolder, "templates"),
                Combine(InstallFolder, @"data\templates"));
        }

        public static string Macros()
        {
            var version = VersionFolder;
            return FirstExisting(
                Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"SOLIDWORKS\" + version + @"\Macros"),
                Combine(InstallFolder, "Macros"),
                Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"SOLIDWORKS\Macros"));
        }

        public static string Toolbox()
        {
            return FirstExisting(
                @"C:\SOLIDWORKS Data",
                @"C:\SOLIDWORKS Data\browser",
                Combine(InstallFolder, "Toolbox"),
                @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS Data");
        }

        private static string Combine(string root, string child)
        {
            return string.IsNullOrEmpty(root) ? string.Empty : Path.Combine(root, child);
        }

        private static string FirstExisting(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates.Length > 0 ? candidates[0] : string.Empty;
        }
    }
}
