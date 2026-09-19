using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using MechKit.Core;

namespace MechKit.Features
{
    /// <summary>读写 SOLIDWORKS 自定义属性。</summary>
    internal static class PropertyService
    {
        /// <summary>文档级属性使用空配置名。</summary>
        public const string DocumentLevelConfiguration = "";

        public static ICustomPropertyManager Manager(ModelDoc2 doc, string configuration)
        {
            if (doc == null)
            {
                return null;
            }

            var extension = doc.Extension;
            if (extension == null)
            {
                return null;
            }

            return extension.CustomPropertyManager[configuration ?? DocumentLevelConfiguration];
        }

        /// <summary>读取文档全部自定义属性（不区分配置，配置名作为属性名前缀）。</summary>
        public static List<CustomProperty> ReadAll(ModelDoc2 doc)
        {
            var result = new List<CustomProperty>();
            if (doc == null)
            {
                return result;
            }

            // 文档级属性（"自定义"选项卡）
            result.AddRange(Read(doc, DocumentLevelConfiguration));

            // 配置特定属性
            try
            {
                var configurationManager = doc.ConfigurationManager;
                var activeName = SwUtils.ActiveConfigurationName(doc);
                if (configurationManager != null && !string.IsNullOrEmpty(activeName))
                {
                    foreach (var property in Read(doc, activeName))
                    {
                        property.Name = "@" + activeName + ":" + property.Name;
                        result.Add(property);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取配置特定属性失败：" + ex.Message);
            }

            return result;
        }

        public static List<CustomProperty> Read(ModelDoc2 doc, string configuration)
        {
            var result = new List<CustomProperty>();
            var manager = Manager(doc, configuration);
            if (manager == null)
            {
                return result;
            }

            string[] names;
            try
            {
                names = ToStringArray(manager.GetNames());
            }
            catch (Exception ex)
            {
                Log.Error("读取属性名失败", ex);
                return result;
            }

            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var property = new CustomProperty { Name = name, Type = PropertyTypes.Text, Value = string.Empty };

                try
                {
                    property.Type = manager.GetType2(name);
                    if (property.Type <= 0 || property.Type > 1000)
                    {
                        property.Type = PropertyTypes.Text;
                    }
                }
                catch
                {
                    property.Type = PropertyTypes.Text;
                }

                try
                {
                    string value;
                    string resolved;
                    bool wasResolved;
                    bool linkToProperty;
                    manager.Get6(name, false, out value, out resolved, out wasResolved, out linkToProperty);

                    property.Value = value ?? string.Empty;
                    property.ResolvedValue = wasResolved && !string.IsNullOrEmpty(resolved) ? resolved : property.Value;
                }
                catch (Exception ex)
                {
                    Log.Warn(string.Format("读取属性「{0}」失败：{1}", name, ex.Message));
                    property.ResolvedValue = string.Empty;
                }

                result.Add(property);
            }

            return result;
        }

        /// <summary>写入属性；overwrite=false 时只在属性不存在时新增。</summary>
        public static bool Write(ModelDoc2 doc, string configuration, CustomProperty property, bool overwrite)
        {
            if (doc == null || property == null || string.IsNullOrEmpty(property.Name))
            {
                return false;
            }

            var manager = Manager(doc, configuration);
            if (manager == null)
            {
                return false;
            }

            var option = overwrite
                ? (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue
                : (int)swCustomPropertyAddOption_e.swCustomPropertyOnlyIfNew;

            var value = PropertyTypes.NormalizeValue(property.Type, property.Value);

            try
            {
                // Add3 同时负责新增与更新，返回码 0 表示已新增或已修改
                var result = manager.Add3(property.Name, property.Type, value, option);
                if (result != (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged)
                {
                    Log.Warn(string.Format("写入属性「{0}」返回：{1}", property.Name,
                        Enum.GetName(typeof(swCustomInfoAddResult_e), result) ?? result.ToString()));
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("写入属性「{0}」失败", property.Name), ex);
                return false;
            }
        }

        public static bool Delete(ModelDoc2 doc, string configuration, string name)
        {
            var manager = Manager(doc, configuration);
            if (manager == null || string.IsNullOrEmpty(name))
            {
                return false;
            }

            try
            {
                var result = manager.Delete2(name);
                return result == (int)swCustomInfoDeleteResult_e.swCustomInfoDeleteResult_OK ||
                       result == (int)swCustomInfoDeleteResult_e.swCustomInfoDeleteResult_NotPresent;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("删除属性「{0}」失败", name), ex);
                return false;
            }
        }

        /// <summary>把属性写入独立文件（自动打开/关闭，不干扰用户当前工作）。</summary>
        public static bool WriteToFile(ISldWorks swApp, string path, IEnumerable<CustomProperty> properties,
            bool overwrite, bool deleteUnlisted, Action<string> log)
        {
            var docType = SwUtils.DocTypeFromPath(path);
            if (docType == 0)
            {
                return false;
            }

            var doc = SwUtils.FindOpenDocument(swApp, path);
            var openedHere = false;

            try
            {
                if (doc == null)
                {
                    int errors = 0;
                    int warnings = 0;
                    doc = swApp.OpenDoc6(path, docType,
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty,
                        ref errors, ref warnings) as ModelDoc2;
                    openedHere = doc != null;
                }

                if (doc == null)
                {
                    return false;
                }

                var configuration = DocumentLevelConfiguration;
                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in Read(doc, configuration))
                {
                    existing.Add(item.Name);
                }

                var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ok = true;
                foreach (var property in properties)
                {
                    if (string.IsNullOrEmpty(property.Name))
                    {
                        continue;
                    }

                    ok &= Write(doc, configuration, property, overwrite);
                    written.Add(property.Name);
                }

                if (deleteUnlisted)
                {
                    foreach (var name in existing)
                    {
                        if (!written.Contains(name))
                        {
                            Delete(doc, configuration, name);
                        }
                    }
                }

                // 静默保存，避免弹出"是否重建"对话框
                try
                {
                    int saveErrors = 0;
                    int saveWarnings = 0;
                    doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref saveErrors, ref saveWarnings);
                }
                catch (Exception ex)
                {
                    Log.Error("保存文档失败：" + path, ex);
                    ok = false;
                }

                return ok;
            }
            catch (Exception ex)
            {
                Log.Error("写入属性失败：" + path, ex);
                return false;
            }
            finally
            {
                if (openedHere && doc != null)
                {
                    try
                    {
                        swApp.CloseDoc(doc.GetTitle());
                    }
                    catch
                    {
                        // 关闭失败不视为致命错误
                    }
                }

                if (doc != null)
                {
                    Release(doc);
                }
            }
        }

        internal static void Release(object comObject)
        {
            try
            {
                if (comObject != null && Marshal.IsComObject(comObject))
                {
                    Marshal.ReleaseComObject(comObject);
                }
            }
            catch
            {
                // 释放失败无需上报
            }
        }

        private static string[] ToStringArray(object value)
        {
            var strings = value as string[];
            if (strings != null)
            {
                return strings;
            }

            var objects = value as object[];
            if (objects == null)
            {
                return new string[0];
            }

            var result = new string[objects.Length];
            for (var i = 0; i < objects.Length; i++)
            {
                result[i] = objects[i] == null ? string.Empty : objects[i].ToString();
            }

            return result;
        }
    }
}
