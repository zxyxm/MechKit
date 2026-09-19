using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MechKit.Core
{
    /// <summary>
    /// 把随包提供的焊件轮廓库（GB焊接轮廓）一键迁移到 SOLIDWORKS 的焊件轮廓目录。
    /// 目标目录在 Program Files 下，需要管理员权限，因此由「安装与卸载.exe」提权执行。
    /// </summary>
    internal static class WeldmentLibrary
    {
        public const string DefaultLibraryName = "GB焊接轮廓";

        /// <summary>插件安装目录（MechKit.dll 所在目录）。</summary>
        public static string InstallRoot
        {
            get
            {
                try
                {
                    return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        /// <summary>随包提供的焊件库源目录。</summary>
        public static string FindSource(AddinSettings settings)
        {
            if (settings != null && !string.IsNullOrEmpty(settings.WeldmentLibrarySource) &&
                Directory.Exists(settings.WeldmentLibrarySource))
            {
                return settings.WeldmentLibrarySource;
            }

            var candidates = new[]
            {
                Path.Combine(InstallRoot, "资源", DefaultLibraryName),
                Path.Combine(InstallRoot, DefaultLibraryName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "MechKit工具箱 V0.1", "资源", DefaultLibraryName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "资源", DefaultLibraryName)
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        /// <summary>SOLIDWORKS 的焊件轮廓根目录。</summary>
        public static string TargetRoot
        {
            get { return SwFolders.WeldmentProfiles(); }
        }

        /// <summary>执行迁移（必要时弹出 UAC 提权）。</summary>
        public static bool Install(AddinSettings settings, out string message)
        {
            var source = FindSource(settings);
            if (string.IsNullOrEmpty(source))
            {
                message = "没有找到随包的焊件库（资源\\" + DefaultLibraryName + "）。\r\n" +
                          "请在「个人配置」里先把焊件库指向该目录，或把库放到插件安装目录的「资源」子目录下。";
                return false;
            }

            var root = TargetRoot;
            if (string.IsNullOrEmpty(root))
            {
                message = "无法定位 SOLIDWORKS 焊件轮廓目录，请确认 SOLIDWORKS 已正确安装。";
                return false;
            }

            var target = Path.Combine(root, Path.GetFileName(source.TrimEnd('\\')));
            var setup = Path.Combine(InstallRoot, "安装与卸载.exe");
            if (!File.Exists(setup))
            {
                message = "找不到提权程序：" + setup + "\r\n请重新运行一次安装程序。";
                return false;
            }

            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = setup,
                    Arguments = string.Format("/weldment \"{0}\" \"{1}\"", source, target),
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using (var process = Process.Start(info))
                {
                    if (process == null)
                    {
                        message = "提权启动失败。";
                        return false;
                    }

                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        message = string.Format("迁移失败（返回码 {0}）。", process.ExitCode);
                        return false;
                    }
                }

                message = string.Format(
                    "焊件库已迁移完成：\r\n{0}\r\n\r\n重启 SOLIDWORKS 后，在焊件特征里就能选到这套轮廓。",
                    target);

                if (settings != null)
                {
                    settings.WeldmentLibrarySource = source;
                    settings.WeldmentFolder = root;
                    settings.Save();
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("迁移焊件库失败", ex);
                message = "迁移失败：" + ex.Message;
                return false;
            }
        }
    }
}
