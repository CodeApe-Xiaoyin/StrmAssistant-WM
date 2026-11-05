using System;
using System.Linq;

namespace StrmAssistant.Common
{
    /// <summary>关键词排除：条目名或路径命中任一关键词就跳过。</summary>
    public static class KeywordFilter
    {
        public static string[] Parse(string keywords) =>
            (keywords ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToArray();

        public static bool Hit(string name, string path, string keywords)
        {
            var list = Parse(keywords);
            if (list.Length == 0) return false;

            name = name ?? string.Empty;
            path = path ?? string.Empty;

            return list.Any(k =>
                name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
