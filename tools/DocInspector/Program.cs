using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace MechKit.Tools
{
    /// <summary>
    /// 诊断工具：连接正在运行的 SOLIDWORKS，
    ///   * 检视零件/装配体的材料与自定义属性
    ///   * 直接调用 MechKit 的 PartListService，验证图号 / 材料 / 加工件数量
    /// 只读打开文档，处理完立即关闭，不保存任何改动。
    /// </summary>
    internal static class Program
    {
        private static bool _useSegmentsEnabled = true;
        private static bool _reloadAddin;
        private static bool _disconnectAfterConnect;
        private static bool _callConnect;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("用法：DocInspector <文件或文件夹> [--assembly] [--limit N] [--csv <输出目录>]");
                return 2;
            }

            var target = args[0];
            var limit = 10;
            var csvFolder = (string)null;
            var asAssembly = false;
            string addinPath = null;
            string addinGuid = null;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--limit":
                        if (i + 1 < args.Length)
                        {
                            int.TryParse(args[++i], out limit);
                        }
                        break;
                    case "--csv":
                        if (i + 1 < args.Length)
                        {
                            csvFolder = args[++i];
                        }
                        break;
                    case "--assembly":
                        asAssembly = true;
                        break;
                    case "--segments":
                        if (i + 1 < args.Length)
                        {
                            _useSegmentsEnabled = !string.Equals(args[++i], "off", StringComparison.OrdinalIgnoreCase);
                        }
                        break;
                    case "--addin":
                        if (i + 1 < args.Length)
                        {
                            addinPath = args[++i];
                        }
                        break;
                    case "--addin-guid":
                        if (i + 1 < args.Length)
                        {
                            addinGuid = args[++i];
                        }
                        break;
                    case "--reload":
                        _reloadAddin = true;
                        break;
                    case "--call-connect":
                        _callConnect = true;
                        break;
                    case "--disconnect":
                        _disconnectAfterConnect = true;
                        break;
                }
            }

            ISldWorks swApp;
            try
            {
                swApp = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
            }
            catch (Exception ex)
            {
                Console.WriteLine("无法连接正在运行的 SOLIDWORKS：" + ex.Message);
                return 3;
            }

            Console.WriteLine("已连接 SOLIDWORKS " + swApp.RevisionNumber());
            Console.WriteLine();

            try
            {
                if (addinPath != null)
                {
                    return InspectAddin(swApp, addinPath, addinGuid);
                }

                if (_callConnect && addinGuid != null)
                {
                    return CallConnect(swApp, addinGuid);
                }

                if (asAssembly || IsAssembly(target))
                {
                    return InspectAssembly(swApp, target, csvFolder);
                }

                if (File.Exists(target))
                {
                    return InspectPart(swApp, target);
                }

                return InspectDocuments(swApp, target, limit);
            }
            catch (Exception ex)
            {
                PrintException(ex);
                return 5;
            }
        }

        private static void PrintException(Exception ex)
        {
            var depth = 0;
            while (ex != null)
            {
                Console.WriteLine(new string(' ', depth * 2) +
                    string.Format("{0}: {1}", ex.GetType().Name, SafeMessage(ex)));
                ex = ex.InnerException;
                depth++;
            }
        }

        private static string SafeMessage(Exception ex)
        {
            try
            {
                return ex.Message;
            }
            catch
            {
                return "(message unavailable)";
            }
        }

        /// <summary>让 SOLIDWORKS 直接加载插件 DLL，并回报加载状态码。</summary>
        private static int InspectAddin(ISldWorks swApp, string dllPath, string guid)
        {
            Console.WriteLine("LoadAddIn : " + dllPath);

            var code = swApp.LoadAddIn(dllPath);
            if (_reloadAddin)
            {
                var unload = swApp.UnloadAddIn(dllPath);
                Console.WriteLine(string.Format("UnloadAddIn: {0} ({1})", unload,
                    Enum.GetName(typeof(swLoadAddinError_e), unload) ?? unload.ToString()));
                System.Threading.Thread.Sleep(1500);
                code = swApp.LoadAddIn(dllPath);
            }

            var name = Enum.GetName(typeof(swLoadAddinError_e), code) ?? code.ToString();
            Console.WriteLine(string.Format("Result    : {0} ({1})", code, name));

            if (guid != null)
            {
                try
                {
                    var addin = swApp.GetAddInObject(guid);
                    Console.WriteLine("AddInObject: " + (addin == null ? "null" : addin.GetType().FullName));

                    if (addin != null && Marshal.IsComObject(addin))
                    {
                        Marshal.ReleaseComObject(addin);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("GetAddInObject failed: " + ex.Message);
                }
            }

            return code == 0 ? 0 : 1;
        }

        /// <summary>
        /// 绕过 SOLIDWORKS 的插件清单，直接创建插件 COM 对象并调用 ConnectToSW，
        /// 用来区分"插件代码问题"和"SOLIDWORKS 加载机制问题"。
        /// </summary>
        private static int CallConnect(ISldWorks swApp, string guid)
        {
            Console.WriteLine("CLSID     : " + guid);

            var clsid = new Guid(guid.Trim('{', '}'));
            var type = Type.GetTypeFromCLSID(clsid);
            Console.WriteLine("COM type  : " + (type == null ? "null" : type.FullName));

            var instance = Activator.CreateInstance(type);
            Console.WriteLine("Instance  : " + instance.GetType().Name);

            var addin = instance as ISwAddin;
            Console.WriteLine("ISwAddin  : " + (addin == null ? "NOT IMPLEMENTED" : "ok"));
            if (addin == null)
            {
                return 1;
            }

            var connected = addin.ConnectToSW(swApp, 1);
            Console.WriteLine("ConnectToSW -> " + connected);

            System.Threading.Thread.Sleep(500);

            var logDirectory = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "MechKit");
            Console.WriteLine("Log folder: " + logDirectory + " exists=" + Directory.Exists(logDirectory));
            if (Directory.Exists(logDirectory))
            {
                foreach (var file in Directory.GetFiles(logDirectory, "*.log", SearchOption.AllDirectories))
                {
                    Console.WriteLine("  " + file);
                }
            }

            if (_disconnectAfterConnect)
            {
                var disconnected = addin.DisconnectFromSW();
                Console.WriteLine("DisconnectFromSW -> " + disconnected);
            }

            return connected ? 0 : 1;
        }

        private static bool IsAssembly(string path)
        {
            return path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
        }

        private static int InspectAssembly(ISldWorks swApp, string path, string csvFolder)
        {
            var doc = OpenReadOnly(swApp, path);
            var assembly = doc as AssemblyDoc;
            if (assembly == null)
            {
                Console.WriteLine("无法打开装配体：" + path);
                return 4;
            }

            var toolkit = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MechKit.dll"));
            var serviceType = toolkit.GetType("MechKit.Features.PartListService", true);
            var optionsType = toolkit.GetType("MechKit.Features.PartListOptions", true);
            var namingType = toolkit.GetType("MechKit.Core.NamingOptions", true);

            Console.WriteLine("MechKit: " + toolkit.Location);

            var options = Activator.CreateInstance(optionsType);
            SetProperty(optionsType, options, "OnlyMachined", false);
            SetProperty(optionsType, options, "ExcludeToolbox", true);
            SetProperty(optionsType, options, "ExcludeSuppressed", true);
            SetProperty(optionsType, options, "ReadCustomProperties", true);

            var namingProperty = optionsType.GetProperty("Naming");
            if (namingProperty == null)
            {
                Console.WriteLine("PartListOptions.Naming 属性缺失。");
                return 6;
            }

            var naming = namingProperty.GetValue(options);

            var cutProperty = namingType.GetProperty("Cut");
            if (cutProperty != null)
            {
                cutProperty.SetValue(naming, Enum.Parse(cutProperty.PropertyType, "FirstSpace"));
            }

            // 按「日期_材料_名称」分段解析名称与材料
            var useSegments = namingType.GetProperty("UseNameSegments");
            if (useSegments != null && _useSegmentsEnabled)
            {
                useSegments.SetValue(naming, true);
                namingType.GetProperty("SegmentSeparator").SetValue(naming, "_");
                namingType.GetProperty("NameSegment").SetValue(naming, -1);
                namingType.GetProperty("MaterialSegment").SetValue(naming, -2);
            }

            var method = serviceType.GetMethod("FromAssembly", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                Console.WriteLine("PartListService.FromAssembly 方法缺失。");
                return 6;
            }

            var log = new Action<string>(Console.WriteLine);

            Console.WriteLine("正在统计组件数量…");
            var rows = (IList)method.Invoke(null, new object[] { swApp, assembly, options, log });

            Console.WriteLine();
            Console.WriteLine("{0,-22} {1,-24} {2,-16} {3,4}  {4}", "图号", "名称", "材料", "数量", "类型");
            Console.WriteLine(new string('-', 92));

            foreach (var row in rows)
            {
                Console.WriteLine("{0,-22} {1,-24} {2,-16} {3,4}  {4}",
                    Truncate(Get(row, "PartNumber"), 22),
                    Truncate(Get(row, "Name"), 24),
                    Truncate(Get(row, "Material"), 16),
                    Get(row, "Quantity"),
                    Get(row, "Classification"));
            }

            Console.WriteLine(new string('-', 92));

            var summary = serviceType.GetMethod("BuildSummary", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { rows });
            Console.WriteLine(summary);

            if (!string.IsNullOrEmpty(csvFolder))
            {
                var csv = serviceType.GetMethod("ExportCsv", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, new object[] { rows, csvFolder, "明细汇总" });
                Console.WriteLine("CSV: " + csv);
            }

            CloseReadOnly(swApp, doc);
            return 0;
        }

        /// <summary>对单个零件运行生产逻辑（PartListService.FromPart），打印解析结果。</summary>
        private static int InspectPart(ISldWorks swApp, string path)
        {
            var doc = OpenReadOnly(swApp, path);
            if (doc == null)
            {
                Console.WriteLine("无法打开：" + path);
                return 4;
            }

            try
            {
                var toolkit = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MechKit.dll"));
                var serviceType = toolkit.GetType("MechKit.Features.PartListService", true);
                var options = BuildOptions(toolkit);
                var method = serviceType.GetMethod("FromPart", BindingFlags.Public | BindingFlags.Static);

                var row = method.Invoke(null, new object[] { swApp, doc, options });
                Console.WriteLine("文件名   : " + Path.GetFileName(path));
                Console.WriteLine("图号     : " + Get(row, "PartNumber"));
                Console.WriteLine("名称     : " + Get(row, "Name"));
                Console.WriteLine("材料     : " + Get(row, "Material"));
                Console.WriteLine("类型     : " + Get(row, "Classification"));
                return 0;
            }
            finally
            {
                CloseReadOnly(swApp, doc);
            }
        }

        private static object BuildOptions(Assembly toolkit)
        {
            var optionsType = toolkit.GetType("MechKit.Features.PartListOptions", true);
            var namingType = toolkit.GetType("MechKit.Core.NamingOptions", true);

            var options = Activator.CreateInstance(optionsType);
            SetProperty(optionsType, options, "OnlyMachined", false);
            SetProperty(optionsType, options, "ExcludeToolbox", true);
            SetProperty(optionsType, options, "ExcludeSuppressed", true);
            SetProperty(optionsType, options, "ReadCustomProperties", true);

            var naming = optionsType.GetProperty("Naming").GetValue(options);
            namingType.GetProperty("Cut")
                .SetValue(naming, Enum.Parse(namingType.GetProperty("Cut").PropertyType, "FirstSpace"));

            if (_useSegmentsEnabled)
            {
                namingType.GetProperty("UseNameSegments").SetValue(naming, true);
                namingType.GetProperty("SegmentSeparator").SetValue(naming, "_");
                namingType.GetProperty("NameSegment").SetValue(naming, -1);
                namingType.GetProperty("MaterialSegment").SetValue(naming, -2);
            }

            return options;
        }

        private static int InspectDocuments(ISldWorks swApp, string target, int limit)
        {
            var files = new List<string>();
            if (Directory.Exists(target))
            {
                foreach (var file in Directory.GetFiles(target))
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension == ".sldprt" || extension == ".sldasm" || extension == ".slddrw")
                    {
                        files.Add(file);
                    }
                }
            }
            else if (File.Exists(target))
            {
                files.Add(target);
            }

            if (files.Count == 0)
            {
                Console.WriteLine("没有可检视的文件：" + target);
                return 4;
            }

            var count = Math.Min(limit, files.Count);
            for (var i = 0; i < count; i++)
            {
                InspectOne(swApp, files[i]);
            }

            Console.WriteLine();
            Console.WriteLine("共检视 {0} / {1} 个文件。", count, files.Count);
            return 0;
        }

        private static void InspectOne(ISldWorks swApp, string file)
        {
            ModelDoc2 doc = null;
            try
            {
                doc = OpenReadOnly(swApp, file);
                if (doc == null)
                {
                    Console.WriteLine("（打不开）" + Path.GetFileName(file));
                    return;
                }

                Console.WriteLine("=== " + Path.GetFileName(file) + " ===");

                if (doc.GetType() == (int)swDocumentTypes_e.swDocPART)
                {
                    Console.WriteLine("  材料(MaterialUserName): '" + (doc.MaterialUserName ?? string.Empty) + "'");
                    Console.WriteLine("  材料(MaterialIdName)  : '" + (doc.MaterialIdName ?? string.Empty) + "'");
                }

                var manager = doc.Extension.CustomPropertyManager[string.Empty];
                var names = manager.GetNames() as string[];
                if (names == null || names.Length == 0)
                {
                    Console.WriteLine("  自定义属性：无");
                }
                else
                {
                    Console.WriteLine("  自定义属性：");
                    foreach (var name in names)
                    {
                        string value;
                        string resolved;
                        bool wasResolved;
                        bool link;
                        manager.Get6(name, false, out value, out resolved, out wasResolved, out link);
                        var type = manager.GetType2(name);
                        Console.WriteLine(string.Format("    {0} = {1}  (type={2})", name,
                            wasResolved && !string.IsNullOrEmpty(resolved) ? resolved : value, type));
                    }
                }

                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine("检视失败：" + Path.GetFileName(file) + " - " + ex.Message);
            }
            finally
            {
                CloseReadOnly(swApp, doc);
            }
        }

        private static ModelDoc2 OpenReadOnly(ISldWorks swApp, string path)
        {
            var docType = 0;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".sldprt":
                    docType = (int)swDocumentTypes_e.swDocPART;
                    break;
                case ".sldasm":
                    docType = (int)swDocumentTypes_e.swDocASSEMBLY;
                    break;
                case ".slddrw":
                    docType = (int)swDocumentTypes_e.swDocDRAWING;
                    break;
                default:
                    return null;
            }

            int errors = 0;
            int warnings = 0;
            return swApp.OpenDoc6(path, docType,
                (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly),
                string.Empty, ref errors, ref warnings) as ModelDoc2;
        }

        private static void CloseReadOnly(ISldWorks swApp, ModelDoc2 doc)
        {
            if (doc == null)
            {
                return;
            }

            try
            {
                swApp.CloseDoc(doc.GetTitle());
            }
            catch
            {
                // 忽略
            }

            try
            {
                if (Marshal.IsComObject(doc))
                {
                    Marshal.ReleaseComObject(doc);
                }
            }
            catch
            {
                // 忽略
            }
        }

        private static void SetProperty(Type type, object instance, string name, object value)
        {
            type.GetProperty(name).SetValue(instance, value);
        }

        private static object Get(object row, string property)
        {
            return row.GetType().GetProperty(property).GetValue(row);
        }

        private static string Truncate(object value, int length)
        {
            var text = value == null ? string.Empty : value.ToString();
            return text.Length <= length ? text : text.Substring(0, length);
        }
    }
}
