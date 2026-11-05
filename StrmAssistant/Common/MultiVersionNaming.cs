using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 多版本美化的命名规则（纯函数，便于单测）。
    ///
    /// Emby 4.10 的多版本不是"一个条目挂多个 MediaSource"，而是多个条目互相引用
    /// （`GetAlternateVersionIds()`）。所以美化要作用在**每个条目自己的名称**上。
    /// </summary>
    public static class MultiVersionNaming
    {
        private static readonly Regex SuffixPattern =
            new Regex(@"\s+-\s+(4K|8K|\d{3,4}p)(\s*\(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 我们自己生成的尾巴：序号「 (2)」、标签「 - 720p H264 (1)」、文件名「 - 影片 (2024)」
        // 只剥 1~2 位的序号「 (1)」…「 (99)」；4 位年份属于标题本身，不能剥
        private static readonly Regex NumericSuffix =
            new Regex(@"\s*\(\d{1,2}\)$", RegexOptions.Compiled);

        private static readonly Regex TagSuffix =
            new Regex(@"\s+-\s+(?<tag>[^-]{1,80})$", RegexOptions.Compiled);

        private static readonly Regex LabelLike =
            new Regex(@"(4K|8K|\d{3,4}p|H\.?26[45]|HEVC|AVC|X26[45]|WEB-?DL|BluRay|REMUX|\(\d{4}\))",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>从媒体信息推断清晰度标签。</summary>
        public static string QualityLabel(int? height, long? bitrate)
        {
            if (height.HasValue && height.Value > 0)
            {
                if (height.Value >= 2000) return "4K";
                if (height.Value >= 1000) return "1080p";
                if (height.Value >= 700) return "720p";
                if (height.Value >= 400) return "480p";
                return height.Value + "p";
            }

            if (bitrate.HasValue && bitrate.Value > 0) return "高码率";

            return "未知";
        }

        /// <summary>去掉已有的" - 1080p"之类后缀，保证重复执行不会叠加。</summary>
        public static string StripSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var current = name.Trim();

            // 反复剥：每次去掉尾部「 (N)」和「 - <标签>」，直到不再变化。
            // 这样重复跑任务不会把名字越加越长（幂等）。
            for (var i = 0; i < 6; i++)
            {
                var before = current;

                // 先按「 - <标签>」剥（标签里可能带年份，先剥才能把年份一起去掉）
                var m = TagSuffix.Match(current);
                if (m.Success && LabelLike.IsMatch(m.Groups["tag"].Value))
                {
                    current = current.Substring(0, m.Index).Trim();
                }
                else
                {
                    current = SuffixPattern.Replace(current, string.Empty).Trim();
                    current = NumericSuffix.Replace(current, string.Empty).Trim();
                }

                if (string.Equals(before, current, StringComparison.Ordinal)) break;
            }

            return current;
        }

        /// <summary>
        /// 为一个版本组计算最终名称：基础名 + 清晰度；同清晰度冲突时追加序号。
        /// </summary>
        public static Dictionary<string, string> ComputeNames(string baseName, IEnumerable<(string Id, string Label)> versions)
        {
            var result = new Dictionary<string, string>();
            var list = versions.ToList();
            var labelCount = list.GroupBy(v => v.Label).ToDictionary(g => g.Key, g => g.Count());
            var seen = new Dictionary<string, int>();

            foreach (var v in list)
            {
                string suffix;

                if (labelCount[v.Label] > 1)
                {
                    seen.TryGetValue(v.Label, out var index);
                    index++;
                    seen[v.Label] = index;
                    suffix = v.Label + " (" + index + ")";
                }
                else
                {
                    suffix = v.Label;
                }

                result[v.Id] = baseName + " - " + suffix;
            }

            return result;
        }

        /// <summary>HDR 类型的简短标签；SDR 或未知返回空串。</summary>
        public static string RangeLabel(string extendedVideoType, string videoRange)
        {
            var ext = (extendedVideoType ?? string.Empty).Trim();
            if (ext.Length > 0 && !string.Equals(ext, "None", StringComparison.OrdinalIgnoreCase))
            {
                switch (ext)
                {
                    case "DolbyVision": return "DV";
                    case "HDR10Plus": return "HDR10+";
                    case "HyperLogGamma": return "HLG";
                    default: return ext;
                }
            }

            var range = (videoRange ?? string.Empty).Trim();
            if (range.Length == 0 || string.Equals(range, "SDR", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return range.Replace(" ", string.Empty);
        }

        /// <summary>音频简短标签：EAC3 5.1 / AAC 2.0。</summary>
        public static string AudioLabel(string codec, int? channels)
        {
            if (string.IsNullOrWhiteSpace(codec)) return string.Empty;
            var c = codec.Trim().ToUpperInvariant();
            if (!channels.HasValue || channels.Value <= 0) return c;
            var ch = channels.Value;
            var layout = ch >= 8 ? "7.1" : ch >= 6 ? "5.1" : ch == 1 ? "1.0" : ch + ".0";
            return c + " " + layout;
        }

        /// <summary>文件大小：25.3GB / 820MB。</summary>
        public static string SizeLabel(long? bytes)
        {
            if (!bytes.HasValue || bytes.Value <= 0) return string.Empty;
            var gb = bytes.Value / 1024d / 1024d / 1024d;
            if (gb >= 1) return gb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "GB";
            var mb = bytes.Value / 1024d / 1024d;
            return Math.Max(1, (int)Math.Round(mb)) + "MB";
        }

        /// <summary>
        /// 版本选择列表里一个版本的标签（只改版本名，不碰条目标题）。
        /// 返回 null 表示「保持 Emby 原来的名字」：跟随系统默认，或者媒体信息还没取到。
        /// </summary>
        public static string VersionLabel(string preference, int? height, long? bitrate, string videoCodec,
            string extendedVideoType, string videoRange, string audioCodec, int? audioChannels, long? size,
            string fileName)
        {
            switch ((preference ?? string.Empty).Trim())
            {
                case "MediaInfo":
                {
                    if (!height.HasValue || height.Value <= 0) return null;
                    var range = RangeLabel(extendedVideoType, videoRange);
                    var q = QualityLabel(height, bitrate);
                    return range.Length > 0 ? q + " " + range : q;
                }
                case "MediaInfoExtended":
                {
                    if (!height.HasValue || height.Value <= 0) return null;
                    var parts = new List<string>();
                    var video = QualityLabel(height, bitrate);
                    if (!string.IsNullOrWhiteSpace(videoCodec)) video += " " + videoCodec.Trim().ToUpperInvariant();
                    var range = RangeLabel(extendedVideoType, videoRange);
                    if (range.Length > 0) video += " " + range;
                    parts.Add(video);
                    var audio = AudioLabel(audioCodec, audioChannels);
                    if (audio.Length > 0) parts.Add(audio);
                    var sz = SizeLabel(size);
                    if (sz.Length > 0) parts.Add(sz);
                    return string.Join(" · ", parts);
                }
                case "FileNameWithoutExtension":
                    return string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim();
                default:
                    return null;
            }
        }

        /// <summary>同名标签追加「 (2)」「 (3)」，保证版本列表里能区分。第一个保持原样。</summary>
        public static List<string> Dedupe(IList<string> labels)
        {
            var result = new List<string>(labels.Count);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var label in labels)
            {
                if (label == null) { result.Add(null); continue; }
                seen.TryGetValue(label, out var n);
                n++;
                seen[label] = n;
                result.Add(n == 1 ? label : label + " (" + n + ")");
            }
            return result;
        }

        private static readonly Regex LegacyLabel =
            new Regex(@"^((4K|8K|\d{3,4}p|未知|高码率)(\s+(HDR10\+?|HDR|DV|HLG|SDR|H\.?264|H\.?265|HEVC|AVC|AV1|VP9))?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 还原被旧版「多版本美化」改写过的标题。旧版把标题改成「基础名 - 标签」，
        /// 而且每次执行都会再叠一层（「X - 未知 - 未知 - X (2024) (1)」）。
        /// 只剥我们自己生成过的尾巴：清晰度标签、序号、以及同组版本的文件名。
        /// 返回 null 表示标题看起来没被改过。
        /// </summary>
        public static string RestoreLegacyTitle(string name, IEnumerable<string> groupFileNames)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var files = (groupFileNames ?? Enumerable.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim())
                .Distinct().OrderByDescending(f => f.Length).ToList();

            var current = name.Trim();

            // 只处理确实被旧任务改过的标题：必须带「 - 清晰度」或「 - 文件名」这种尾巴。
            // 否则「The Best of Both Worlds (1)」这类本来就带序号的标题会被误删序号。
            var hasLegacyMarker = files.Any(f => current.Contains(" - " + f)) ||
                                  current.Split(new[] { " - " }, StringSplitOptions.None).Skip(1)
                                      .Any(part => LegacyLabel.IsMatch(NumericSuffix.Replace(part, string.Empty).Trim()));
            if (!hasLegacyMarker) return null;

            for (var i = 0; i < 64; i++)
            {
                var before = current;
                current = NumericSuffix.Replace(current, string.Empty).Trim();

                var fileHit = files.FirstOrDefault(f => current.EndsWith(" - " + f, StringComparison.Ordinal));
                if (fileHit != null)
                {
                    current = current.Substring(0, current.Length - fileHit.Length - 3).Trim();
                }
                else
                {
                    var idx = current.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (idx > 0 && LegacyLabel.IsMatch(current.Substring(idx + 3).Trim()))
                        current = current.Substring(0, idx).Trim();
                }

                if (string.Equals(before, current, StringComparison.Ordinal)) break;
            }

            if (current.Length == 0 || string.Equals(current, name.Trim(), StringComparison.Ordinal)) return null;
            return current;
        }

        /// <summary>按「多版本排序」偏好排列版本；无偏好时保持原顺序。</summary>
        public static System.Collections.Generic.List<T> OrderVariants<T>(
            System.Collections.Generic.IEnumerable<T> items,
            string preference,
            System.Func<T, long> heightOf,
            System.Func<T, string> pathOf)
        {
            var list = System.Linq.Enumerable.ToList(items);
            switch ((preference ?? string.Empty).Trim())
            {
                case "HighToLowQuality":
                    return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderByDescending(list, heightOf));
                case "LowToHighQuality":
                    return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(list, heightOf));
                case "PathAscending":
                    return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(list, pathOf, System.StringComparer.OrdinalIgnoreCase));
                case "PathDescending":
                    return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderByDescending(list, pathOf, System.StringComparer.OrdinalIgnoreCase));
                default:
                    return list;
            }
        }
    }
}