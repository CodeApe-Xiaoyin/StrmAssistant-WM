using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 「独占式提取」：不依赖 Emby 的刷新流程，自己读 .strm 里的地址、调用媒体探测，
    /// 再把结果写回条目。用于 Emby 探测不到远端流的情况。
    /// </summary>
    public static class StrmProbe
    {
        /// <summary>
        /// 找可用的 ffprobe：优先选项里指定的，其次 Emby 配置的，最后常见路径。
        /// 生产上 FfmpegConfiguration 拿不到（日志 "FfmpegConfiguration 不可用"），
        /// 需要靠这条兜底，否则独占式探测全部失败。
        /// </summary>
        private static string ResolveProbeBinary(string custom)
        {
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;

            try
            {
                var ffmpeg = Plugin.Instance?.GetType().GetProperty("FfmpegManager",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public)?.GetValue(Plugin.Instance);
                var config = ffmpeg?.GetType().GetProperty("FfmpegConfiguration")?.GetValue(ffmpeg);
                var probePath = config?.GetType().GetProperty("ProbePath")?.GetValue(config) as string;
                if (!string.IsNullOrWhiteSpace(probePath) && File.Exists(probePath)) return probePath;
            }
            catch
            {
                // 忽略，继续找常见路径
            }

            foreach (var candidate in new[] { "/bin/ffprobe", "/usr/bin/ffprobe", "/system/ffprobe" })
                if (File.Exists(candidate)) return candidate;

            return null;
        }

        /// <param name="force">修复损坏的媒体信息时用：不要求开启「独占式提取」。</param>
        public static async Task<bool> TryProbeAsync(BaseItem item, ILogger logger, CancellationToken cancellationToken,
            bool force = false)
        {
            var options = Plugin.Instance?.GetPluginOptions()?.MediaInfoExtractOptions;
            if (options == null) return false;
            if (options.ExclusiveExtract != true && !force) return false;
            if (item == null) return false;
            if (string.IsNullOrWhiteSpace(item.Path)) return false;

            // CatchAllAllow：允许对非 STRM（本地媒体）也走这套探测
            var allowAll = options.HasControl(
                Options.MediaInfoExtractOptions.ExclusiveControl.CatchAllAllow);

            string url;
            if (item.IsShortcut)
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(item.Path, cancellationToken).ConfigureAwait(false);
                    url = lines.Select(l => l?.Trim()).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                }
                catch (Exception e)
                {
                    logger?.Info("StrmProbe - 读取 strm 失败: " + item.Name + " " + e.Message);
                    return false;
                }
            }
            else if (allowAll)
            {
                // 本地文件直接当输入；不是远端协议
                url = item.Path;
            }
            else
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(url)) return false;

            var timeoutMs = options.MediaInfoExtractionTimeoutMs <= 0 ? 25000 : options.MediaInfoExtractionTimeoutMs;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            var isRemote = item.IsShortcut;
            var source = new MediaSourceInfo
            {
                Path = url,
                Protocol = isRemote ? MediaProtocol.Http : MediaProtocol.File,
                IsRemote = isRemote,
                Type = MediaSourceType.Default
            };

            var request = new MediaInfoRequest
            {
                MediaSource = source,
                MediaType = DlnaProfileType.Video,
                ExtractChapters = false
            };

            try
            {
                MediaSourceInfo info;

                var customProbe = ResolveProbeBinary(options.CustomProbePath);
                if (customProbe != null)
                {
                    info = await ProbeWithProcessAsync(customProbe, url, logger, cts.Token).ConfigureAwait(false);
                    if (info != null) logger?.Info("StrmProbe - 使用 ffprobe: " + customProbe);
                }
                else
                {
                    try
                    {
                        info = await Plugin.MediaProbeManager.GetMediaInfo(request, cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Emby 的探测在拿不到 ffprobe 配置时会抛异常（生产实测过），退回自带 ffprobe
                        logger?.Info("StrmProbe - Emby 探测失败: " + ex.Message);
                        info = await ProbeWithProcessAsync(ResolveProbeBinary(null) ?? "/bin/ffprobe", url, logger, cts.Token)
                            .ConfigureAwait(false);
                    }
                }

                if (info?.MediaStreams == null || info.MediaStreams.Count == 0)
                {
                    logger?.Info("StrmProbe - 探测无结果: " + item.Name + " -> " + url);
                    return false;
                }

                item.MediaStreams = info.MediaStreams;
                if (info.RunTimeTicks.HasValue) item.RunTimeTicks = info.RunTimeTicks;

                // 容器 / 大小 / 总码率：Emby 判断能否直接播放要用。以前没写，条目被当成 .strm 容器。
                var container = !string.IsNullOrEmpty(info.Container)
                    ? NormalizeContainer(info.Container, url)
                    : NormalizeContainer(null, url);
                if (!string.IsNullOrEmpty(container)) item.Container = container;
                if (info.Size.HasValue && info.Size.Value > 0) item.Size = info.Size.Value;
                if (info.Bitrate.HasValue && info.Bitrate.Value > 0) item.TotalBitrate = info.Bitrate.Value;


                Plugin.LibraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit, null);

                // 再尝试把流写进库（持久化/片头标记依赖它）。失败不影响探测本身。
                try
                {
                    Plugin.MediaInfoApi.PersistStreams(item.InternalId, info.MediaStreams);
                }
                catch (Exception persistEx)
                {
                    var inner = persistEx is System.Reflection.TargetInvocationException tie && tie.InnerException != null
                        ? tie.InnerException : persistEx;
                    var detail = inner.ToString();
                    if (detail.Length > 400) detail = detail.Substring(0, 400);
                    logger?.Info("StrmProbe - 流落库失败: " + detail);
                }

                var video = info.MediaStreams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
                logger?.Info("StrmProbe - 命中: " + item.Name + " | " + (video?.Codec ?? "?") + " " +
                             (video?.Width?.ToString() ?? "?") + "x" + (video?.Height?.ToString() ?? "?") +
                             " | run=" + (info.RunTimeTicks?.ToString() ?? "?") + " | 容器=" + (item.Container ?? "?") +
                             " | 流=" + info.MediaStreams.Count);

                return true;
            }
            catch (OperationCanceledException)
            {
                logger?.Info("StrmProbe - 超时(" + timeoutMs + "ms): " + item.Name);
                return false;
            }
            catch (Exception e)
            {
                logger?.Info("StrmProbe - 失败: " + item.Name + " " + e.Message);
                return false;
            }
        }
        /// <summary>
        /// 用指定 ffprobe 可执行文件探测（自定义路径 / Emby 探测失败时的兜底）。
        /// 以前只解析了 codec / 宽高 / 声道：没有容器、大小、码率，也没有流序号、语言、标题、默认标记、HDR 信息，
        /// 结果是 Emby 把条目当成 .strm 容器去判断能否直接播放（4K HEVC MKV 在浏览器里直接播放失败），
        /// 多条音轨也只显示成一排没名字的 "AAC 2ch"。
        /// </summary>
        private static async Task<MediaSourceInfo> ProbeWithProcessAsync(string probePath, string url,
            ILogger logger, CancellationToken cancellationToken)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = probePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in new[] { "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", url })
                psi.ArgumentList.Add(arg);

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw;
            }

            var json = await stdoutTask.ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? null : ParseFfprobeJson(json, url);
        }

        /// <summary>把 ffprobe -show_format -show_streams 的 JSON 转成 MediaSourceInfo（单独拆出来便于测试）。</summary>
        public static MediaSourceInfo ParseFfprobeJson(string json, string url)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var info = new MediaSourceInfo { Path = url, Protocol = MediaProtocol.Http, IsRemote = true };
            var streams = new System.Collections.Generic.List<MediaStream>();

            if (root.TryGetProperty("streams", out var streamArray))
            {
                foreach (var st in streamArray.EnumerateArray())
                {
                    var codecType = Str(st, "codec_type");
                    MediaStreamType type;
                    switch (codecType)
                    {
                        case "video":
                            // 封面图（mjpeg/png 带 attached_pic）不是视频轨
                            type = Disposition(st, "attached_pic") ? MediaStreamType.EmbeddedImage : MediaStreamType.Video;
                            break;
                        case "audio": type = MediaStreamType.Audio; break;
                        case "subtitle": type = MediaStreamType.Subtitle; break;
                        case "attachment":
                            // MKV 里的字体等附件：Emby 有 Attachment 类型就用，没有就不收（以前被误标成内嵌图片）
                            if (!Enum.TryParse("Attachment", out type)) continue;
                            break;
                        default:
                            continue; // data 流等，Emby 不需要
                    }

                    var stream = new MediaStream
                    {
                        Index = Int(st, "index") ?? streams.Count,
                        Type = type,
                        Codec = Str(st, "codec_name"),
                        Profile = Str(st, "profile"),
                        Language = Tag(st, "language"),
                        Title = Tag(st, "title"),
                        IsDefault = Disposition(st, "default"),
                        IsForced = Disposition(st, "forced"),
                        IsHearingImpaired = Disposition(st, "hearing_impaired"),
                        IsExternal = false,
                        Width = Int(st, "width"),
                        Height = Int(st, "height"),
                        Channels = Int(st, "channels"),
                        ChannelLayout = Str(st, "channel_layout"),
                        SampleRate = Int(st, "sample_rate"),
                        BitRate = Int(st, "bit_rate") ?? IntTag(st, "BPS"),
                        PixelFormat = Str(st, "pix_fmt"),
                        BitDepth = Int(st, "bits_per_raw_sample"),
                        Level = Int(st, "level"),
                        ColorTransfer = Str(st, "color_transfer"),
                        ColorPrimaries = Str(st, "color_primaries"),
                        ColorSpace = Str(st, "color_space"),
                        AspectRatio = Str(st, "display_aspect_ratio"),
                        TimeBase = Str(st, "time_base"),
                        CodecTag = Str(st, "codec_tag_string"),
                        AverageFrameRate = Rate(Str(st, "avg_frame_rate")),
                        RealFrameRate = Rate(Str(st, "r_frame_rate")),
                        IsInterlaced = Str(st, "field_order") is string fo && fo != "progressive" && fo != "unknown"
                    };

                    // 10bit HEVC 常常没有 bits_per_raw_sample，从像素格式推断
                    if (stream.BitDepth == null && stream.PixelFormat != null)
                        stream.BitDepth = stream.PixelFormat.Contains("10") ? 10 : stream.PixelFormat.Contains("12") ? 12 : (int?)null;

                    streams.Add(stream);
                }
            }

            info.MediaStreams = streams;

            if (root.TryGetProperty("format", out var format))
            {
                if (Str(format, "duration") is string dur && double.TryParse(dur,
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                {
                    info.RunTimeTicks = (long)(seconds * TimeSpan.TicksPerSecond);
                }

                if (Str(format, "bit_rate") is string fbr && long.TryParse(fbr, out var totalBitrate))
                    info.Bitrate = (int)Math.Min(int.MaxValue, totalBitrate);

                if (Str(format, "size") is string sz && long.TryParse(sz, out var size))
                    info.Size = size;

                info.Container = NormalizeContainer(Str(format, "format_name"), url);
            }

            return streams.Count == 0 ? null : info;
        }

        /// <summary>ffprobe 的 format_name（如 "matroska,webm"、"mov,mp4,m4a,3gp,3g2,mj2"）转成 Emby 用的容器名。</summary>
        public static string NormalizeContainer(string formatName, string url)
        {
            var ext = string.Empty;
            try
            {
                var path = Uri.TryCreate(url, UriKind.Absolute, out var u) ? Uri.UnescapeDataString(u.AbsolutePath) : url;
                ext = Path.GetExtension(path ?? string.Empty).TrimStart('.').ToLowerInvariant();
            }
            catch
            {
                // 取不到扩展名就只看 format_name
            }

            var names = (formatName ?? string.Empty).Split(',').Select(n => n.Trim().ToLowerInvariant()).ToArray();
            if (names.Contains("matroska")) return ext == "webm" ? "webm" : "mkv";
            if (names.Contains("mov") || names.Contains("mp4")) return ext == "mov" ? "mov" : ext == "m4v" ? "m4v" : "mp4";
            if (names.Contains("mpegts")) return ext == "m2ts" || ext == "mts" ? "m2ts" : "ts";
            if (names.Contains("avi")) return "avi";
            if (names.Contains("flv")) return "flv";
            if (names.Contains("asf")) return "wmv";
            if (names.Contains("mpeg")) return "mpeg";
            if (!string.IsNullOrEmpty(ext) && ext != "strm") return ext;
            return names.FirstOrDefault(n => n.Length > 0);
        }

        private static string Str(System.Text.Json.JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() :
            e.TryGetProperty(name, out v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetRawText() : null;

        private static int? Int(System.Text.Json.JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == System.Text.Json.JsonValueKind.String && long.TryParse(v.GetString(), out var l))
                return (int)Math.Min(int.MaxValue, l);
            return null;
        }

        private static string Tag(System.Text.Json.JsonElement e, string name)
        {
            if (!e.TryGetProperty("tags", out var tags) || tags.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            foreach (var p in tags.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return string.IsNullOrWhiteSpace(p.Value.GetString()) ? null : p.Value.GetString();
            return null;
        }

        private static int? IntTag(System.Text.Json.JsonElement e, string name) =>
            Tag(e, name) is string t && long.TryParse(t, out var l) ? (int)Math.Min(int.MaxValue, l) : (int?)null;

        private static bool Disposition(System.Text.Json.JsonElement e, string name) =>
            e.TryGetProperty("disposition", out var d) && d.TryGetProperty(name, out var v) &&
            v.ValueKind == System.Text.Json.JsonValueKind.Number && v.GetInt32() == 1;

        private static float? Rate(string r)
        {
            if (string.IsNullOrEmpty(r)) return null;
            var parts = r.Split('/');
            if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) && b > 0)
                return a / b;
            return null;
        }
    }
}
