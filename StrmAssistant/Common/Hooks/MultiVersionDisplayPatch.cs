using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 「美化多版本显示」：改的是**版本选择列表**里每个版本的名字和顺序，不碰条目标题。
    ///
    /// Emby 播放页 / 详情页的版本下拉框来自 `BaseItem.GetMediaSources(...)` 返回的列表，
    /// 每个 MediaSourceInfo.Name 就是下拉框里的文字（默认取文件名去掉文件夹前缀）。
    /// 这里在它返回后：
    ///   · 按「多版本显示方式」改写 Name（4K HDR10 / 4K HEVC HDR10 · EAC3 5.1 · 25.3GB / 文件名）；
    ///   · 按「多版本排序」重排列表（排第一的就是默认播放的版本）。
    /// 只处理多于一个版本的结果；单版本、开关关闭时原样返回。
    ///
    /// 旧实现是定时任务直接把条目标题改成「名称 - 清晰度」，标题会出现在媒体库列表里，
    /// 而且重复执行会越叠越长；版本下拉框反而没变。
    /// </summary>
    public static class MultiVersionDisplayPatch
    {
        public static bool Installed { get; private set; }

        [ThreadStatic] private static bool _busy;

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var postfix = typeof(MultiVersionDisplayPatch).GetMethod(nameof(Postfix),
                    BindingFlags.NonPublic | BindingFlags.Static);

                var r = HarmonyHelper.PatchPostfix(typeof(BaseItem), "GetMediaSources", postfix,
                    "StrmAssistant.MultiVersionDisplay", out var patched);

                Installed = patched > 0;
                return "GetMediaSources:" + (patched > 0 ? "ok(" + patched + ")" : "none") + " " + r;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static void Postfix(ref List<MediaSourceInfo> __result)
        {
            if (_busy || __result == null || __result.Count < 2) return;

            try
            {
                _busy = true;

                var options = Plugin.Instance?.CurrentOptions?.ExperienceEnhanceOptions;
                if (options?.BeautifyMultiVersion != true) return;

                __result = Apply(__result, options.MultiVersionDisplayPreference,
                    options.MultiVersionReorderPreference, ResolveFileName);
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Debug("MultiVersionDisplay - 处理失败: " + e.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>改名 + 排序（纯逻辑，单测直接调）。</summary>
        public static List<MediaSourceInfo> Apply(List<MediaSourceInfo> sources, string displayPreference,
            string sortPreference, Func<MediaSourceInfo, string> fileNameOf)
        {
            if (sources == null || sources.Count < 2) return sources;

            var display = (displayPreference ?? string.Empty).Trim();
            if (display.Length > 0 && !string.Equals(display, "SystemDefault", StringComparison.Ordinal))
            {
                var labels = sources.Select(s =>
                {
                    var video = s.MediaStreams?.FirstOrDefault(x => x.Type == MediaStreamType.Video);
                    var audio = s.MediaStreams?.Where(x => x.Type == MediaStreamType.Audio)
                        .OrderByDescending(x => x.IsDefault).FirstOrDefault();
                    return MultiVersionNaming.VersionLabel(display, video?.Height, s.Bitrate, video?.Codec,
                        video?.ExtendedVideoType.ToString(), video?.VideoRange, audio?.Codec, audio?.Channels,
                        s.Size, display == "FileNameWithoutExtension" ? fileNameOf?.Invoke(s) : null);
                }).ToList();

                // 有一个版本缺媒体信息就整体不改：一半美化一半文件名比原来更乱
                if (labels.All(l => l != null))
                {
                    var unique = MultiVersionNaming.Dedupe(labels);
                    for (var i = 0; i < sources.Count; i++) sources[i].Name = unique[i];
                }
            }

            var sort = (sortPreference ?? string.Empty).Trim();
            if (sort.Length > 0 && !string.Equals(sort, "SystemDefault", StringComparison.Ordinal))
            {
                sources = MultiVersionNaming.OrderVariants(sources, sort,
                    s => (long)(s.MediaStreams?.FirstOrDefault(x => x.Type == MediaStreamType.Video)?.Height ?? 0),
                    s => fileNameOf?.Invoke(s) ?? s.Path ?? string.Empty);
            }

            return sources;
        }

        /// <summary>版本对应的文件名（strm 取 .strm 文件名，而不是里面的网址）。</summary>
        private static string ResolveFileName(MediaSourceInfo source)
        {
            string path = null;
            try
            {
                if (long.TryParse(source.ItemId, out var internalId))
                    path = Plugin.LibraryManager?.GetItemById(internalId)?.Path;
                else if (Guid.TryParse(source.Id, out var guid))
                    path = Plugin.LibraryManager?.GetItemById(guid)?.Path;
            }
            catch
            {
                // 取不到就用媒体源自己的路径
            }

            path = string.IsNullOrWhiteSpace(path) ? source.Path : path;
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (path.Contains("://"))
            {
                var q = path.IndexOfAny(new[] { '?', '#' });
                if (q >= 0) path = path.Substring(0, q);
                path = Uri.UnescapeDataString(path.Substring(path.LastIndexOf('/') + 1));
            }

            return System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }
}
