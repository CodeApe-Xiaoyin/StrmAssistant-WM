using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO：简繁转换（对应「优先繁体」选项）。
    /// </summary>
    public static class ChineseTextHelper
    {
        public static bool ContainsChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var ch in text)
                if (ch >= 0x4E00 && ch <= 0x9FFF) return true;
            return false;
        }

        public static string ToTraditional(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return ChineseConverter.Convert(text,
                ChineseConversionDirection.SimplifiedToTraditional);
        }

        public static string ToSimplified(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return ChineseConverter.Convert(text,
                ChineseConversionDirection.TraditionalToSimplified);
        }
    }
}
