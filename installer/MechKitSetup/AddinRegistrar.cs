using System;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

namespace MechKit.Setup
{
    internal sealed class AddinInfo
    {
        public string Guid;
        public string ProgId;
        public string Title;
        public string Description;
        public string TypeName;
        public string AssemblyName;
        public string AssemblyDisplayName;
        public string Version;
        public string DllPath;
    }

    /// <summary>
    /// 与 SolidWorksAddinInstaller.exe / regasm 相同的注册方式：
    /// COM 注册写 HKLM\SOFTWARE\Classes\CLSID，插件清单写 HKLM\SOFTWARE\SolidWorks\AddIns。
    /// </summary>
    internal sealed class AddinRegistrar
    {
        private const string DotNetCategory = "{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}";
        private const string RuntimeVersion = "v4.0.30319";
        // 必须是裸文件名：InprocServer32 是 REG_SZ，不会展开 %SystemRoot% 之类的
        // 环境变量，写成 %SystemRoot%\system32\mscoree.dll 会导致
        // CoCreateInstance 返回 0x8007007E（找不到指定的模块）。
        // regasm 与嘉立创 Ican 工具箱用的都是裸文件名。
        private const string Mscoree = "mscoree.dll";

        public AddinRegistrar(string payloadDirectory, string installDirectory)
        {
            PayloadDirectory = payloadDirectory;
            InstallDirectory = installDirectory;
        }

        /// <summary>
        /// 默认安装到不含空格的目录：注册表里的 CodeBase 是纯文本路径，
        /// 空格与 %20 转义在部分 SOLIDWORKS 版本上会导致加载失败
        /// （嘉立创 Ican 工具箱用的也是这种无空格目录）。
        /// </summary>
        public static string DefaultInstallDirectory
        {
            get { return @"C:\MechKit"; }
        }

        /// <summary>把本地路径转成 CodeBase 用的 file:/// 形式（不转义，保持可读路径）。</summary>
        public static string ToCodeBase(string path)
        {
            return "file:///" + path.Replace('\\', '/');
        }

        /// <summary>分发目录（安装包所在目录）。</summary>
        public string PayloadDirectory { get; private set; }

        /// <summary>目标安装目录，默认 C:\Program Files\MechKit。</summary>
        public string InstallDirectory { get; private set; }

        public string InstalledDllPath
        {
            get { return Path.Combine(InstallDirectory, "MechKit.dll"); }
        }

        public static bool IsElevated
        {
            get
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                    .IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public AddinInfo ReadInfo(string dllPath)
        {
            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException("找不到 " + dllPath);
            }

            // 用字节数组加载：Assembly.LoadFrom 会一直锁住文件，
            // 导致安装程序开着的时候无法重新打包/覆盖这个 DLL。
            var assembly = Assembly.Load(File.ReadAllBytes(dllPath));
            var type = assembly.GetType("MechKit.MechKitAddin", false);
            if (type == null)
            {
                throw new InvalidOperationException("该文件不是 MechKit插件：" + dllPath);
            }

            var constants = assembly.GetType("MechKit.AddinConstants", false);

            return new AddinInfo
            {
                Guid = "{" + type.GUID.ToString().ToUpperInvariant() + "}",
                ProgId = (string)constants.GetField("ProgId").GetValue(null),
                Title = (string)constants.GetField("Title").GetValue(null),
                Description = (string)constants.GetField("Description").GetValue(null),
                TypeName = type.FullName,
                AssemblyName = assembly.GetName().Name,
                AssemblyDisplayName = assembly.FullName,
                Version = assembly.GetName().Version.ToString(),
                DllPath = dllPath
            };
        }

        public bool IsInstalled(out string installedVersion)
        {
            installedVersion = null;

            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = baseKey.OpenSubKey(@"SOFTWARE\SolidWorks\AddIns\" + AddinGuid))
            {
                if (key == null)
                {
                    return false;
                }
            }

            if (File.Exists(InstalledDllPath))
            {
                try
                {
                    installedVersion = AssemblyName.GetAssemblyName(InstalledDllPath).Version.ToString();
                }
                catch
                {
                    installedVersion = "unknown";
                }
            }

            return true;
        }

        private static string AddinGuid
        {
            get { return "{A7F3C1E2-5B4D-4E8A-9C21-3D6F0B7A5E10}"; }
        }

        public void Install(AddinInfo info, bool startWithSolidWorks, Action<string> log)
        {
            if (!IsElevated)
            {
                throw new InvalidOperationException("需要管理员权限。");
            }

            // 1. 复制载荷（插件本体 + 互操作程序集）
            if (!Directory.Exists(InstallDirectory))
            {
                Directory.CreateDirectory(InstallDirectory);
            }

            foreach (var name in new[]
            {
                "MechKit.dll",
                "SolidWorks.Interop.sldworks.dll",
                "SolidWorks.Interop.swconst.dll",
                "SolidWorks.Interop.swpublished.dll"
            })
            {
                var source = Path.Combine(PayloadDirectory, name);
                if (!File.Exists(source))
                {
                    continue;
                }

                File.Copy(source, Path.Combine(InstallDirectory, name), true);
                log("复制 " + name);
            }

            // 把安装程序本身也放进安装目录：插件要用它提权执行「一键迁移焊件库」
            try
            {
                var setup = Assembly.GetEntryAssembly() == null ? null : Assembly.GetEntryAssembly().Location;
                if (!string.IsNullOrEmpty(setup) && File.Exists(setup))
                {
                    var target = Path.Combine(InstallDirectory, "安装与卸载.exe");
                    if (!string.Equals(setup, target, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(setup, target, true);
                        log("复制 安装与卸载.exe");
                    }
                }
            }
            catch (Exception ex)
            {
                log("复制安装程序失败（不影响插件使用）：" + ex.Message);
            }

            var codeBase = ToCodeBase(InstalledDllPath);

            // 2. 机器级 COM 注册（等价于 regasm /codebase）
            //
            // 注意：本机（以及不少加固过的系统）上 HKLM\SOFTWARE\Classes 这个键本身
            // 不可写，只有它的子键可以创建，所以这里必须一次性给出完整路径，
            // 不能先 CreateSubKey(@"SOFTWARE\Classes") 再往下建。
            const string classesPath = @"SOFTWARE\Classes\CLSID\";
            var clsidPath = classesPath + info.Guid;
            var inprocPath = clsidPath + @"\InprocServer32";

            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                WriteKey(machine, clsidPath, null, info.ProgId);

                WriteKey(machine, inprocPath, null, Mscoree);
                WriteKey(machine, inprocPath, "ThreadingModel", "Both");
                WriteKey(machine, inprocPath, "Class", info.TypeName);
                WriteKey(machine, inprocPath, "Assembly", info.AssemblyDisplayName);
                WriteKey(machine, inprocPath, "RuntimeVersion", RuntimeVersion);
                WriteKey(machine, inprocPath, "CodeBase", codeBase);

                var versionedPath = inprocPath + @"\" + info.Version;
                WriteKey(machine, versionedPath, "Class", info.TypeName);
                WriteKey(machine, versionedPath, "Assembly", info.AssemblyDisplayName);
                WriteKey(machine, versionedPath, "RuntimeVersion", RuntimeVersion);
                WriteKey(machine, versionedPath, "CodeBase", codeBase);

                WriteKey(machine, clsidPath + @"\ProgId", null, info.ProgId);
                WriteKey(machine, clsidPath + @"\Implemented Categories\" + DotNetCategory, null, string.Empty);
                WriteKey(machine, @"SOFTWARE\Classes\" + info.ProgId, null, info.ProgId);
                WriteKey(machine, @"SOFTWARE\Classes\" + info.ProgId + @"\CLSID", null, info.Guid);
            }

            log("COM 注册完成：HKLM\\SOFTWARE\\Classes\\CLSID\\" + info.Guid);

            // 3. 插件清单 + 随 SOLIDWORKS 启动
            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                using (var addin = machine.CreateSubKey(@"SOFTWARE\SolidWorks\AddIns\" + info.Guid))
                {
                    addin.SetValue(null, 0, RegistryValueKind.DWord);
                    addin.SetValue("Title", info.Title, RegistryValueKind.String);
                    addin.SetValue("Description", info.Description, RegistryValueKind.String);
                }

                using (var startup = machine.CreateSubKey(@"SOFTWARE\SolidWorks\AddInsStartup\" + info.Guid))
                {
                    startup.SetValue(null, startWithSolidWorks ? 1 : 0, RegistryValueKind.DWord);
                }
            }

            using (var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            {
                using (var startup = user.CreateSubKey(@"SOFTWARE\SolidWorks\AddInsStartup\" + info.Guid))
                {
                    startup.SetValue(null, startWithSolidWorks ? 1 : 0, RegistryValueKind.DWord);
                }

                // 清掉开发期可能留下的当前用户注册，避免旧路径抢占
                TryDelete(user, @"Software\Classes\CLSID\" + info.Guid);
                TryDelete(user, @"Software\Classes\" + info.ProgId);
                TryDelete(user, @"SOFTWARE\SolidWorks\AddIns\" + info.Guid);
            }

            log("SOLIDWORKS 插件清单写入完成：HKLM\\SOFTWARE\\SolidWorks\\AddIns\\" + info.Guid);
            log(startWithSolidWorks ? "已设置为随 SOLIDWORKS 启动。" : "未设置随 SOLIDWORKS 启动。");
        }

        public void Uninstall(bool removeFiles, Action<string> log)
        {
            var guid = AddinGuid;
            string progId;

            try
            {
                progId = ReadInfo(File.Exists(InstalledDllPath)
                    ? InstalledDllPath
                    : Path.Combine(PayloadDirectory, "MechKit.dll")).ProgId;
            }
            catch
            {
                progId = "MechKit.Addin";
            }

            using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                TryDelete(machine, @"SOFTWARE\Classes\CLSID\" + guid);
                TryDelete(machine, @"SOFTWARE\Classes\" + progId);
                TryDelete(machine, @"SOFTWARE\SolidWorks\AddIns\" + guid);
                TryDelete(machine, @"SOFTWARE\SolidWorks\AddInsStartup\" + guid);
                log("已清除机器级注册。");
            }

            using (var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            {
                TryDelete(user, @"SOFTWARE\Classes\CLSID\" + guid);
                TryDelete(user, @"SOFTWARE\Classes\" + progId);
                TryDelete(user, @"SOFTWARE\SolidWorks\AddIns\" + guid);
                TryDelete(user, @"SOFTWARE\SolidWorks\AddInsStartup\" + guid);
                log("已清除当前用户注册。");
            }

            if (removeFiles && Directory.Exists(InstallDirectory))
            {
                var marker = Path.Combine(InstallDirectory, "MechKit.dll");
                if (File.Exists(marker))
                {
                    Directory.Delete(InstallDirectory, true);
                    log("已删除安装目录：" + InstallDirectory);
                }
                else
                {
                    log("安装目录未包含 MechKit.dll，跳过删除：" + InstallDirectory);
                }
            }
        }

        private static void TryDelete(RegistryKey root, string path)
        {
            try
            {
                root.DeleteSubKeyTree(path, false);
            }
            catch
            {
                // 不存在或权限不足都可以忽略
            }
        }

        /// <summary>直接按完整路径写值；路径上的父键不存在时一起创建。</summary>
        private static void WriteKey(RegistryKey root, string path, string name, string value)
        {
            using (var key = root.CreateSubKey(path))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法创建注册表项：HKLM\\" + path);
                }

                if (name == null)
                {
                    key.SetValue(null, value ?? string.Empty, RegistryValueKind.String);
                }
                else
                {
                    key.SetValue(name, value ?? string.Empty, RegistryValueKind.String);
                }
            }
        }
    }
}
