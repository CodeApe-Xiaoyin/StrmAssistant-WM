using System.Text;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 把媒体条目名整理成适合搜索的形式：
    /// 全角转半角、去掉年份/花絮括号、压缩空白。
    /// 例：「沙丘（2021）」→「沙丘」；「ＳＱＵＩＤ　ＧＡＭＥ」→「SQUID GAME」。
    /// </summary>
    public static class SearchNameNormalizer
    {
        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var halfWidth = ToHalfWidth(name);
            var withoutBrackets = RemoveBracketGroups(halfWidth);

            var candidate = string.IsNullOrWhiteSpace(withoutBrackets) ? halfWidth : withoutBrackets;
            return CollapseSpaces(candidate).Trim();
        }

        internal static string ToHalfWidth(string value)
        {
            var sb = new StringBuilder(value.Length);

            foreach (var ch in value)
            {
                if (ch == '\u3000')
                {
                    sb.Append(' ');
                }
                else if (ch >= '\uFF01' && ch <= '\uFF5E')
                {
                    sb.Append((char) (ch - 0xFEE0));
                }
                else
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        /// <summary>去掉括号及其中的内容（含半角/全角圆括号与方括号）。</summary>
        internal static string RemoveBracketGroups(string value)
        {
            var sb = new StringBuilder(value.Length);
            var depth = 0;

            foreach (var ch in value)
            {
                if (ch == '(' || ch == '[' || ch == '{' || ch == '\u3010')
                {
                    depth++;
                    continue;
                }

                if (ch == ')' || ch == ']' || ch == '}' || ch == '\u3011')
                {
                    if (depth > 0) depth--;
                    continue;
                }

                if (depth == 0) sb.Append(ch);
            }

            return sb.ToString();
        }

        internal static string CollapseSpaces(string value)
        {
            var sb = new StringBuilder(value.Length);
            var lastSpace = false;

            foreach (var ch in value)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastSpace && sb.Length > 0) sb.Append(' ');
                    lastSpace = true;
                }
                else
                {
                    sb.Append(ch);
                    lastSpace = false;
                }
            }

            return sb.ToString();
        }
    }
}
