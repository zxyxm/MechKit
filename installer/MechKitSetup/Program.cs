using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace MechKit.Setup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var payload = AppDomain.CurrentDomain.BaseDirectory;
            var install = AddinRegistrar.DefaultInstallDirectory;
            var registrar = new AddinRegistrar(payload, install);

            // Silent mode, used by scripts and by the automated tests:
            //   安装与卸载.exe /install /log <file>
            //   安装与卸载.exe /uninstall /removefiles /log <file>
            if (args != null && args.Length > 0 && args[0].StartsWith("/", StringComparison.Ordinal))
            {
                Environment.ExitCode = RunCommandLine(registrar, args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(registrar));
        }

        private static int RunCommandLine(AddinRegistrar registrar, string[] args)
        {
            var options = new List<string>();
            foreach (var arg in args)
            {
                options.Add(arg.ToLowerInvariant());
            }

            var logPath = GetOptionValue(args, "/log");
            var messages = new List<string>();
            Action<string> log = delegate(string message)
            {
                messages.Add(message);
            };

            var install = options.Contains("/install");
            var uninstall = options.Contains("/uninstall");
            var removeFiles = options.Contains("/removefiles");
            var noStartup = options.Contains("/nostartup");
            var weldmentSource = GetOptionValue(args, "/weldment");
            var weldmentTarget = GetOptionValue(args, "/to");

            var exitCode = 0;

            try
            {
                messages.Add("MechKit 安装程序（命令行模式）");
                messages.Add("管理员权限：" + AddinRegistrar.IsElevated);

                if (!AddinRegistrar.IsElevated)
                {
                    throw new InvalidOperationException("需要管理员权限运行。");
                }

                if (install)
                {
                    var info = registrar.ReadInfo(Path.Combine(registrar.PayloadDirectory, "MechKit.dll"));
                    registrar.Install(info, !noStartup, log);
                    messages.Add("安装完成。");
                }
                else if (uninstall)
                {
                    registrar.Uninstall(removeFiles, log);
                    messages.Add("卸载完成。");
                }
                else if (!string.IsNullOrEmpty(weldmentSource))
                {
                    // 一键迁移焊件库：把源目录整棵复制到 SOLIDWORKS 的焊件轮廓目录
                    var target = weldmentTarget;
                    if (string.IsNullOrEmpty(target))
                    {
                        throw new InvalidOperationException("缺少 /to <目标目录> 参数。");
                    }

                    var copied = CopyTree(weldmentSource, target);
                    messages.Add(string.Format("焊件库已迁移 {0} 个文件到：{1}", copied, target));
                }
                else
                {
                    messages.Add("用法：/install 或 /uninstall [/removefiles] [/nostartup] [/log <文件>]");
                    messages.Add("      /weldment <源目录> /to <目标目录> [/log <文件>]");
                    exitCode = 2;
                }
            }
            catch (Exception ex)
            {
                messages.Add("失败：" + ex.Message);
                exitCode = 1;
            }

            if (!string.IsNullOrEmpty(logPath))
            {
                try
                {
                    File.WriteAllText(logPath, string.Join(Environment.NewLine, messages.ToArray()));
                }
                catch
                {
                    // 写日志失败不影响退出码
                }
            }

            return exitCode;
        }

        private static string GetOptionValue(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        /// <summary>递归复制目录，返回复制的文件数。</summary>
        private static int CopyTree(string source, string target)
        {
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException("源目录不存在：" + source);
            }

            var copied = 0;
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
            }

            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                copied++;
            }

            foreach (var directory in Directory.GetDirectories(source))
            {
                copied += CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
            }

            return copied;
        }
    }
}
