using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MechKit.Core
{
    /// <summary>
    /// 个人设置迁移：把 SOLIDWORKS 的个人配置（界面、笔势、快捷键、文件位置、
    /// 导出选项等，全部在 HKCU\Software\SolidWorks\SOLIDWORKS &lt;年份&gt; 下）
    /// 导出成一个文件，再在别的 SOLIDWORKS 上导入。
    /// </summary>
    internal static class SettingsTransfer
    {
        public const string FileExtension = ".mechkit";

        /// <summary>列出当前用户下所有 SOLIDWORKS 版本的设置键（HKCU 相对路径）。</summary>
        public static List<string> FindSwSettingsKeys()
        {
            var result = new List<string>();

            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\SolidWorks"))
                {
                    if (root == null)
                    {
                        return result;
                    }

                    foreach (var name in root.GetSubKeyNames())
                    {
                        if (name.StartsWith("SOLIDWORKS ", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(@"HKCU\Software\SolidWorks\" + name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("枚举 SOLIDWORKS 设置键失败", ex);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>导出个人设置（含笔势、快捷键、文件位置）到指定文件。</summary>
        public static bool Export(string targetFile, bool includeAddinSettings, out string message)
        {
            var keys = FindSwSettingsKeys();
            if (keys.Count == 0)
            {
                message = "没有找到 SOLIDWORKS 个人设置（注册表 HKCU\\Software\\SolidWorks 下没有版本键）。";
                return false;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Windows Registry Editor Version 5.00");
            builder.AppendLine();
            builder.AppendLine("; MechKit 个人设置备份");
            builder.AppendLine("; 由 MechKit 导出，导入方式：MechKit → 个人配置 → 导入设置");
            builder.AppendLine();

            var exported = 0;
            foreach (var key in keys)
            {
                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".reg");
                try
                {
                    var exit = RunReg(new[] { "export", key, temp, "/y" });
                    if (exit != 0 || !File.Exists(temp))
                    {
                        Log.Warn("导出注册表失败：" + key);
                        continue;
                    }

                    // reg export 会写入自己的文件头，去掉后拼到同一个文件里
                    var lines = File.ReadAllLines(temp, Encoding.Unicode);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Windows Registry Editor", StringComparison.OrdinalIgnoreCase) ||
                            line.Trim().Length == 0)
                        {
                            continue;
                        }

                        builder.AppendLine(line);
                    }

                    builder.AppendLine();
                    exported++;
                }
                finally
                {
                    try { if (File.Exists(temp)) { File.Delete(temp); } } catch { }
                }
            }

            if (exported == 0)
            {
                message = "导出失败：没有可写入的内容。";
                return false;
            }

            try
            {
                File.WriteAllText(targetFile, builder.ToString(), Encoding.Unicode);
            }
            catch (Exception ex)
            {
                message = "写入文件失败：" + ex.Message;
                return false;
            }

            if (includeAddinSettings)
            {
                try
                {
                    var side = targetFile + ".mechkit-settings.ini";
                    if (File.Exists(AppPaths.SettingsFile))
                    {
                        File.Copy(AppPaths.SettingsFile, side, true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("附带 MechKit 设置失败：" + ex.Message);
                }
            }

            message = string.Format("已导出 {0} 个设置分支（含界面、笔势、快捷键、文件位置）：{1}",
                exported, targetFile);
            return true;
        }

        /// <summary>导入个人设置。</summary>
        public static bool Import(string sourceFile, out string message)
        {
            if (!File.Exists(sourceFile))
            {
                message = "文件不存在：" + sourceFile;
                return false;
            }

            var exit = RunReg(new[] { "import", sourceFile });
            if (exit != 0)
            {
                message = string.Format("导入失败（reg.exe 返回 {0}）。文件必须是 MechKit 导出的 .mechkit/reg 文件。", exit);
                return false;
            }

            // 附带恢复 MechKit 自身设置
            var side = sourceFile + ".mechkit-settings.ini";
            if (File.Exists(side))
            {
                try
                {
                    AppPaths.Ensure(AppPaths.Root);
                    File.Copy(side, AppPaths.SettingsFile, true);
                }
                catch (Exception ex)
                {
                    Log.Warn("恢复 MechKit 设置失败：" + ex.Message);
                }
            }

            message = "导入完成。请关闭并重新启动 SOLIDWORKS，界面、笔势、快捷键与文件位置才会生效。";
            return true;
        }

        private static int RunReg(string[] arguments)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                foreach (var argument in arguments)
                {
                    info.Arguments += (info.Arguments.Length == 0 ? string.Empty : " ") + Quote(argument);
                }

                using (var process = Process.Start(info))
                {
                    if (process == null)
                    {
                        return -1;
                    }

                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(30000);
                    return process.HasExited ? process.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                Log.Error("调用 reg.exe 失败", ex);
                return -1;
            }
        }

        private static string Quote(string value)
        {
            return value.IndexOf(' ') < 0 ? value : "\"" + value + "\"";
        }
    }
}
