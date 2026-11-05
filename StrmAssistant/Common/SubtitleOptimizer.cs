using System;
using System.IO;
using System.Text;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 字幕优化：统一转成 UTF-8（无 BOM），可选把繁体转简体。
    /// 只处理常见的纯文本字幕格式（srt / ass / ssa / vtt）。
    /// </summary>
    public static class SubtitleOptimizer
    {
        private static readonly string[] Supported = { ".srt", ".ass", ".ssa", ".vtt" };

        static SubtitleOptimizer()
        {
            // .NET Core 默认不带 GB18030/GBK 等代码页，中文字幕多数是这类编码，必须先注册
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
            catch { /* 注册失败则退回 UTF-8 探测 */ }
        }

        public static bool IsSupported(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = Path.GetExtension(path);
            return Array.IndexOf(Supported, ext?.ToLowerInvariant()) >= 0;
        }

        /// <summary>找出条目同目录下的字幕文件（用于下载后批量优化）。</summary>
        public static System.Collections.Generic.List<string> FindNearbySubtitles(string itemPath)
        {
            var result = new System.Collections.Generic.List<string>();

            try
            {
                if (string.IsNullOrWhiteSpace(itemPath)) return result;

                var dir = Directory.Exists(itemPath) ? itemPath : Path.GetDirectoryName(itemPath);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return result;

                foreach (var file in Directory.GetFiles(dir))
                {
                    if (IsSupported(file)) result.Add(file);
                }
            }
            catch
            {
                // 目录不可读时忽略
            }

            return result;
        }

        /// <summary>返回是否发生了改动。</summary>
        public static bool Optimize(string path, bool preferSimplified, out string message)
        {
            message = string.Empty;

            if (!File.Exists(path))
            {
                message = "文件不存在";
                return false;
            }

            if (!IsSupported(path))
            {
                message = "不支持的格式";
                return false;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                var text = Decode(bytes);

                // ToSimplified 对简体文本是幂等的，直接比较前后差异即可
                if (preferSimplified && ChineseTextHelper.ContainsChinese(text))
                {
                    var simplified = ChineseTextHelper.ToSimplified(text);
                    if (!string.Equals(simplified, text, StringComparison.Ordinal))
                    {
                        text = simplified;
                        message += "已转简体;";
                    }
                }

                var utf8 = new UTF8Encoding(false).GetBytes(text);

                if (!Equal(bytes, utf8))
                {
                    File.WriteAllBytes(path, utf8);
                    message += "已转 UTF-8;";
                    return true;
                }

                message += "无需改动;";
                return false;
            }
            catch (Exception e)
            {
                message = "失败: " + e.Message;
                return false;
            }
        }

        private static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch
            {
                // 不是合法 UTF-8，按 GB18030 兜底（中文字幕常见）
                try { return Encoding.GetEncoding("GB18030").GetString(bytes); }
                catch { return Encoding.Default.GetString(bytes); }
            }
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
