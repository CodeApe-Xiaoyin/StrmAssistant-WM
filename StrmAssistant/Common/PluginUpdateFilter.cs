using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Updates;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 「抑制指定插件更新」的过滤逻辑。
    /// 设置里填的是插件名列表（逗号或分号分隔，如 MovieDb,Tvdb），
    /// 这里按包名 / 目标文件名匹配（忽略大小写与 .dll 后缀）。
    /// </summary>
    public static class PluginUpdateFilter
    {
        public static string[] ParseNames(string config)
        {
            if (string.IsNullOrWhiteSpace(config)) return Array.Empty<string>();

            return config
                .Split(new[] { ',', ';', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();
        }

        public static bool ShouldSuppress(PackageVersionInfo package, IEnumerable<string> names)
        {
            if (package == null || names == null) return false;

            foreach (var raw in names)
            {
                var name = raw.Trim();
                if (name.Length == 0) continue;

                if (Matches(package.name, name)) return true;
                if (Matches(package.targetFilename, name)) return true;
            }

            return false;
        }

        private static bool Matches(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            var normalized = value.Trim();
            if (normalized.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);

            return string.Equals(normalized, name, StringComparison.OrdinalIgnoreCase);
        }

        public static PackageVersionInfo[] Apply(PackageVersionInfo[] packages, string config)
        {
            var names = ParseNames(config);
            if (names.Length == 0 || packages == null) return packages;

            return packages.Where(p => !ShouldSuppress(p, names)).ToArray();
        }
    }
}
