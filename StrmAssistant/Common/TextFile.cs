using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 读取外部文本文件（NFO / JSON 等）时的编码兜底。
    /// 真实世界里中文用户提供的 NFO 常是 GBK/GB18030，纯 UTF-8 读取会得到乱码或直接抛异常。
    /// </summary>
    public static class TextFile
    {
        static TextFile()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        public static string ReadAllText(string path)
        {
            if (!File.Exists(path)) return null;

            var bytes = File.ReadAllBytes(path);
            return Decode(bytes);
        }

        public static XDocument LoadXml(string path)
        {
            var text = ReadAllText(path);
            if (string.IsNullOrEmpty(text)) return null;

            try { return XDocument.Parse(text); }
            catch { return null; }
        }

        public static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

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
                // 不是合法 UTF-8：中文场景优先按 GB18030 解
                try { return Encoding.GetEncoding("GB18030").GetString(bytes); }
                catch { return Encoding.Default.GetString(bytes); }
            }
        }
    }
}
