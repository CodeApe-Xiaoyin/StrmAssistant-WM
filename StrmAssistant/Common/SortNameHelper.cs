using TinyPinyin;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO：用拼音首字母生成排序标题（对应「拼音排序标题」选项）。
    /// </summary>
    public static class SortNameHelper
    {
        /// <summary>名称里是否含中文（只有含中文才需要拼音排序）。</summary>
        public static bool ContainsChinese(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var ch in name)
                if (PinyinHelper.IsChinese(ch)) return true;
            return false;
        }

        /// <summary>PRO 语义：用拼音首字母作为排序标题。</summary>
        public static string BuildSortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;

            var initials = PinyinHelper.GetPinyinInitials(name);
            if (string.IsNullOrWhiteSpace(initials)) return name;

            // 排序键去掉标点（全角冒号、括号、斜杠等）：它们按码位排序会插到字母中间，
            // 让「007：幽灵党」和「007 幽灵党」分到相距很远的位置。
            var normalized = NormalizeSortName(initials);
            return string.IsNullOrWhiteSpace(normalized) ? initials : normalized;
        }

        /// <summary>只保留字母、数字与空格，其余（含全角标点）丢弃。</summary>
        internal static string NormalizeSortName(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var sb = new System.Text.StringBuilder(value.Length);
            var lastWasSpace = false;

            foreach (var ch in value)
            {
                var isLetterOrDigit = (ch >= '0' && ch <= '9') ||
                                      (ch >= 'a' && ch <= 'z') ||
                                      (ch >= 'A' && ch <= 'Z');

                if (isLetterOrDigit)
                {
                    sb.Append(ch);
                    lastWasSpace = false;
                }
                else if (char.IsWhiteSpace(ch))
                {
                    if (sb.Length > 0 && !lastWasSpace) sb.Append(' ');
                    lastWasSpace = true;
                }
                // 其它字符（标点、符号）直接丢弃
            }

            return sb.ToString().Trim();
        }
    }
}
