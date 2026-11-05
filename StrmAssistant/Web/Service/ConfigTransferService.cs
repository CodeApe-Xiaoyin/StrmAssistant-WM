using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    /// <summary>
    /// 配置导出/导入（迁移用）。只搬运设置数据，编辑器元数据全部跳过。
    /// 采用「扁平键值对」形式（如 MetadataEnhanceOptions.PinyinSortName = true），
    /// 避免嵌套序列化在 Emby 的响应管道里出问题。
    /// </summary>
    public class ConfigTransferService : IService
    {
        private static readonly string[] SectionNames =
        {
            "GeneralOptions", "MediaInfoExtractOptions", "MetadataEnhanceOptions",
            "IntroSkipOptions", "ExperienceEnhanceOptions", "DashboardOptions"
        };

        public object Get(ConfigExportApi request)
        {
            var options = Plugin.Instance.GetPluginOptions();
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (var name in SectionNames)
            {
                var section = options.GetType().GetProperty(name)?.GetValue(options);
                if (section != null) Flatten(section, name, values, 0);
            }

            return new
            {
                exportedAt = DateTimeOffset.UtcNow.ToString("u"),
                pluginVersion = Plugin.Instance.Version?.ToString(),
                count = values.Count,
                values
            };
        }

        public object Post(ConfigImportApi request)
        {
            try
            {
                string body;
                using (var buffer = new System.IO.MemoryStream())
                {
                    request.RequestStream.CopyToAsync(buffer).GetAwaiter().GetResult();
                    body = Encoding.UTF8.GetString(buffer.ToArray());
                }

                if (string.IsNullOrWhiteSpace(body)) return new { ok = false, error = "请求体为空" };

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("values", out var valuesElement) ||
                    valuesElement.ValueKind != JsonValueKind.Object)
                {
                    return new { ok = false, error = "缺少 values 字段" };
                }

                var options = Plugin.Instance.GetPluginOptions();
                var restored = 0;
                var failed = new List<string>();
                var rejected = new List<string>();

                foreach (var entry in valuesElement.EnumerateObject())
                {
                    try
                    {
                        if (Apply(options, entry.Name, entry.Value.GetString())) restored++;
                        else rejected.Add(entry.Name);
                    }
                    catch (Exception e)
                    {
                        failed.Add(entry.Name + ": " + e.GetBaseException().Message);
                    }
                }

                if (restored > 0) Plugin.Instance.SavePluginOptionsSuppress();

                // 保存后回读校验：把"写了但没落盘"的情况暴露出来，而不是静默成功
                var mismatched = new List<string>();
                if (restored > 0)
                {
                    var after = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var name in SectionNames)
                    {
                        var section = options.GetType().GetProperty(name)?.GetValue(options);
                        if (section != null) Flatten(section, name, after, 0);
                    }

                    foreach (var entry in valuesElement.EnumerateObject())
                    {
                        if (!after.TryGetValue(entry.Name, out var actual)) continue;
                        var expected = entry.Value.GetString();
                        if (expected != null && actual != null &&
                            !string.Equals(actual, expected, StringComparison.Ordinal) &&
                            !actual.Equals(entry.Value.ToString(), StringComparison.Ordinal))
                        {
                            mismatched.Add(entry.Name);
                        }
                    }
                }

                return new
                {
                    ok = restored > 0,
                    valuesRestored = restored,
                    rejected = rejected.Count == 0 ? null : string.Join(" | ", rejected.Take(10)),
                    rejectedCount = rejected.Count,
                    verified = restored - mismatched.Count,
                    mismatched = mismatched.Count == 0 ? null : string.Join(" | ", mismatched.Take(10)),
                    mismatchedCount = mismatched.Count,
                    failed = failed.Count == 0 ? null : string.Join(" | ", failed.Take(5))
                };
            }
            catch (Exception e)
            {
                return new { ok = false, error = e.GetBaseException().Message };
            }
        }

        private static void Flatten(object source, string prefix, IDictionary<string, string> output, int depth)
        {
            if (source == null || depth > 3) return;

            foreach (var prop in source.GetType().GetProperties())
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                if (Ignored.Contains(prop.Name)) continue;

                var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                var value = prop.GetValue(source);

                // 先判断标量（枚举也是标量），否则本插件命名空间里的枚举会被误当成子对象递归掉
                if (IsScalar(type))
                {
                    if (value == null) continue;
                    output[prefix + "." + prop.Name] = type.IsEnum
                        ? value.ToString()
                        : Convert.ToString(value, CultureInfo.InvariantCulture);
                    continue;
                }

                if (type.Namespace != null && type.Namespace.StartsWith("StrmAssistant", StringComparison.Ordinal))
                {
                    if (value != null) Flatten(value, prefix + "." + prop.Name, output, depth + 1);
                }
            }
        }

        /// <summary>只读或纯展示字段，不参与迁移。</summary>
        private static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "EditorTitle", "EditorDescription", "IsNewItem", "FeatureRequiresPremiere",
            "CurrentUsageMb", "EditorRoot", "DefaultObject"
        };

        private static bool IsOwnType(Type type) =>
            type.Namespace != null && type.Namespace.StartsWith("StrmAssistant", StringComparison.Ordinal);

        private static bool IsScalar(Type type) =>
            type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type.IsEnum;

        /// <summary>把扁平键值写回对象；非法路径/类型一律返回 false，不抛异常。</summary>
        internal static bool Apply(object root, string path, string raw)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var parts = path.Split('.');

            // 至少要有「分组.字段」两层，避免直接改根对象
            if (parts.Length < 2) return false;

            object current = root;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                var prop = current == null ? null : current.GetType().GetProperty(parts[i]);
                if (prop == null) return false;

                // 只允许在本插件自己的类型里下钻，避免顺着属性爬进 .NET 框架对象
                if (!IsOwnType(prop.PropertyType)) return false;

                var child = prop.GetValue(current);
                if (child == null)
                {
                    child = Activator.CreateInstance(prop.PropertyType);
                    if (child == null) return false;
                    prop.SetValue(current, child);
                }

                current = child;
            }

            if (current == null) return false;

            var leaf = current.GetType().GetProperty(parts[parts.Length - 1]);
            if (leaf == null || !leaf.CanWrite) return false;
            if (Ignored.Contains(leaf.Name)) return false;

            var type = Nullable.GetUnderlyingType(leaf.PropertyType) ?? leaf.PropertyType;

            if (raw == null)
            {
                // 值类型不接受 null
                if (leaf.PropertyType.IsValueType && Nullable.GetUnderlyingType(leaf.PropertyType) == null)
                    return false;

                leaf.SetValue(current, null);
                return true;
            }

            try
            {
                object value;
                if (type == typeof(string)) value = raw;
                else if (type == typeof(bool)) value = bool.Parse(raw);
                else if (type.IsEnum) value = Enum.Parse(type, raw, true);
                else value = Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);

                leaf.SetValue(current, value);
                return true;
            }
            catch
            {
                // 值不合法就跳过这一条，不影响其它键
                return false;
            }
        }
    }
}
