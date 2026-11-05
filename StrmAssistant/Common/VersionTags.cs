using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 从文件名里取「版本标签」：清晰度、HDR、编码、来源、音轨。
    /// 两个文件名的标签集合不同，就不是同一版本（不管名字整体有多像）。
    /// </summary>
    public static class VersionTags
    {
        private static readonly Regex Token = new Regex(
            @"(?<![A-Za-z0-9])(4320p|2160p|1440p|1080[pi]|720p|576[pi]|480p|8K|4K|UHD|HDR10\+|HDR10|HDR|DV|DoVi|HLG|SDR|" +
            @"x26[45]|h\.?26[45]|HEVC|AVC|AV1|VP9|10bit|8bit|REMUX|BluRay|Blu-Ray|BDRip|WEB-?DL|WEBRip|HDTV|DVDRip|" +
            @"Atmos|TrueHD|DTS-HD|DTS|DDP?5\.1|E?AC3|AAC|FLAC)(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static HashSet<string> Of(string fileName)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(fileName)) return set;

            foreach (Match m in Token.Matches(fileName))
            {
                var t = m.Value.ToUpperInvariant().Replace(".", string.Empty).Replace("-", string.Empty);
                if (t == "X264" || t == "H264") t = "AVC";
                if (t == "X265" || t == "H265") t = "HEVC";
                if (t == "DOVI") t = "DV";
                set.Add(t);
            }

            return set;
        }
    }
}
