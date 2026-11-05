using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 提前提取媒体信息的两个入口（都只是把条目放进「提取媒体信息」队列，真正提取仍由 QueueManager 按并发、范围、排除规则处理）：
    ///   · 预加载：开始播放某一集时，把下一集放进队列；
    ///   · 浏览时提取：打开视频 / 音频详情页时，把这个条目放进队列（没有媒体信息的才会真正被提取）。
    /// 同一个条目 10 分钟内只入队一次，避免反复打开详情页时重复排队。
    /// </summary>
    public static class MediaInfoPrefetch
    {
        private static readonly ConcurrentDictionary<long, DateTime> Recent = new ConcurrentDictionary<long, DateTime>();
        private static bool _subscribed;
        public static bool BrowseHookInstalled { get; private set; }

        public static void Initialize(ISessionManager sessionManager)
        {
            if (_subscribed || sessionManager == null) return;
            _subscribed = true;
            sessionManager.PlaybackStart += OnPlaybackStart;
        }

        private static void OnPlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            try
            {
                if (Plugin.Instance?.CurrentOptions?.MediaInfoExtractOptions?.EnableMediaInfoPreload != true) return;
                if (!(e?.Item is Episode episode)) return;

                var next = NextEpisode(episode);
                if (next == null) return;

                if (Enqueue(next))
                    Plugin.Instance?.Logger?.Info("MediaInfoPreload - 播放「" + episode.Name + "」，预加载下一集「" + next.Name + "」");
            }
            catch (Exception ex)
            {
                Plugin.Instance?.Logger?.Info("MediaInfoPreload - 失败: " + ex.Message);
            }
        }

        /// <summary>同一部剧里按「季号、集号」排在它后面的第一集；特别篇（第 0 季）只在看特别篇时才算。</summary>
        internal static Episode NextEpisode(Episode current)
        {
            var series = current.Series;
            if (series == null || Plugin.LibraryManager == null) return null;

            var all = Plugin.LibraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { nameof(Episode) },
                    AncestorIds = new[] { series.InternalId },
                    Recursive = true
                })
                .OfType<Episode>()
                .Where(ep => ep.IndexNumber.HasValue && ep.InternalId != current.InternalId)
                .Where(ep => (current.ParentIndexNumber ?? 0) == 0 || (ep.ParentIndexNumber ?? 0) > 0)
                .ToList();

            return PickNext(all, current.ParentIndexNumber ?? 0, current.IndexNumber ?? 0);
        }

        internal static Episode PickNext(System.Collections.Generic.IEnumerable<Episode> episodes, int season, int index) =>
            episodes
                .Where(ep => (ep.ParentIndexNumber ?? 0) > season ||
                             ((ep.ParentIndexNumber ?? 0) == season && ep.IndexNumber.Value > index))
                .OrderBy(ep => ep.ParentIndexNumber ?? 0).ThenBy(ep => ep.IndexNumber)
                .FirstOrDefault();

        /// <summary>放进提取队列（10 分钟内去重）。返回是否真的入队了。</summary>
        public static bool Enqueue(BaseItem item)
        {
            if (item == null || !(item is Video || item is Audio)) return false;

            var now = DateTime.UtcNow;
            if (Recent.TryGetValue(item.InternalId, out var at) && (now - at).TotalMinutes < 10) return false;
            if (Recent.Count > 5000) Recent.Clear();
            Recent[item.InternalId] = now;

            QueueManager.EnsureMediaInfoProcessing();
            QueueManager.MediaInfoExtractItemQueue.Enqueue(item);
            return true;
        }

        /// <summary>挂在详情页接口 UserLibraryService.Get(GetItem) 之后：看到视频 / 音频就入队。</summary>
        public static string TryInstallBrowseHook()
        {
            if (BrowseHookInstalled) return "already";
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Emby.Api") ??
                          Assembly.Load("Emby.Api");
                var type = asm.GetType("Emby.Api.UserLibrary.UserLibraryService");
                var method = type?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Get" && m.GetParameters().Length == 1 &&
                                         m.GetParameters()[0].ParameterType.Name == "GetItem");
                if (method == null) return "找不到 UserLibraryService.Get(GetItem)";

                new HarmonyLib.Harmony("StrmAssistant.BrowseExtract").Patch(method,
                    postfix: new HarmonyLib.HarmonyMethod(typeof(MediaInfoPrefetch).GetMethod(nameof(BrowsePostfix),
                        BindingFlags.NonPublic | BindingFlags.Static)));
                BrowseHookInstalled = true;
                return "ok";
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static void BrowsePostfix(object[] __args)
        {
            try
            {
                if (Plugin.Instance?.CurrentOptions?.MediaInfoExtractOptions?.ExtractOnBrowse != true) return;

                var request = __args?.FirstOrDefault();
                var id = request?.GetType().GetProperty("Id")?.GetValue(request)?.ToString();
                if (string.IsNullOrWhiteSpace(id)) return;

                var lm = Plugin.LibraryManager;
                var item = long.TryParse(id, out var internalId)
                    ? lm?.GetItemById(internalId)
                    : Guid.TryParse(id, out var guid) ? lm?.GetItemById(guid) : null;
                if (item == null) return;

                // 已有媒体信息的不入队（队列那边也会再判断一次）
                if (item is Video video && video.GetMediaStreams()?.Any(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video) == true)
                    return;

                if (Enqueue(item))
                    Plugin.Instance?.Logger?.Info("ExtractOnBrowse - 浏览「" + item.Name + "」，没有媒体信息，已加入提取队列");
            }
            catch
            {
                // 详情页接口上的钩子绝不能影响页面本身
            }
        }
    }
}
