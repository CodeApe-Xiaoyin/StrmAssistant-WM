using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.EntryPoints
{
    /// <summary>
    /// 新内容即时处理（对应 PRO 的「扫描时自动刮削」）。
    /// 用 <see cref="ILibraryManager.ItemAdded"/> 这个官方事件，不需要挂钩：
    /// 条目入库后先入队，后台批量处理，避免拖慢媒体库扫描本身。
    /// </summary>
    public class NewContentEntryPoint : IServerEntryPoint
    {
        private const int DebounceSeconds = 10;
        private const int MaxBatch = 50;

        private static readonly ConcurrentQueue<BaseItem> Pending = new ConcurrentQueue<BaseItem>();

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private CancellationTokenSource _cts;

        public NewContentEntryPoint(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public void Run()
        {
            _libraryManager.ItemAdded += OnItemAdded;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => WorkerAsync(_cts.Token));
            _logger.Info("NewContent - subscribed to ItemAdded");
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            try
            {
                var item = e?.Item;
                if (item == null) return;
                if (!(item is Episode || item is Series || item is Movie)) return;

                var enabled = Plugin.Instance?.GetPluginOptions()?.IntroSkipOptions?
                    .EnableAutomaticIntroScraping ?? false;
                if (!enabled) return;

                Pending.Enqueue(item);
            }
            catch (Exception ex)
            {
                _logger.Info("NewContent - enqueue failed: " + ex.Message);
            }
        }

        private async Task WorkerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(DebounceSeconds), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var batch = new System.Collections.Generic.List<BaseItem>();
                while (batch.Count < MaxBatch && Pending.TryDequeue(out var item)) batch.Add(item);
                if (batch.Count == 0) continue;

                _logger.Info("NewContent - processing " + batch.Count + " new item(s)");

                foreach (var item in batch)
                {
                    if (token.IsCancellationRequested) return;

                    try
                    {
                        if (item is Episode episode)
                        {
                            await ProcessEpisodeAsync(episode, token).ConfigureAwait(false);
                        }
                        else if (item is Series series)
                        {
                            var query = new InternalItemsQuery
                            {
                                Recursive = true,
                                IncludeItemTypes = new[] { typeof(Episode).Name },
                                AncestorIds = new[] { series.InternalId }
                            };

                            foreach (var ep in _libraryManager.GetItemList(query).OfType<Episode>())
                            {
                                if (token.IsCancellationRequested) return;
                                await ProcessEpisodeAsync(ep, token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Info("NewContent - failed for " + item.Name + ": " + ex.Message);
                    }
                }
            }
        }

        private async Task ProcessEpisodeAsync(Episode episode, CancellationToken token)
        {
            var series = episode.Series;
            if (series == null) return;

            var itemRepository = Plugin.ItemRepository;
            if (itemRepository == null) return;

            // 已有片头标记就不再重复抓
            var existing = itemRepository.GetChapters(episode, token);
            if (existing.Any(c => c.MarkerType == MediaBrowser.Model.Entities.MarkerType.IntroStart)) return;

            if (!series.ProviderIds.TryGetValue("Imdb", out var imdbId) || string.IsNullOrEmpty(imdbId)) return;

            var api = new Common.IntroDbApi(Plugin.HttpClient);
            var marker = await api.GetMarkerAsync(imdbId, episode.ParentIndexNumber ?? 0,
                episode.IndexNumber ?? 0, token).ConfigureAwait(false);

            if (marker == null) return;

            if (IntroDbApplyServiceHelper.Apply(itemRepository, episode, marker))
            {
                _logger.Info($"NewContent - markers applied to {episode.Name} ({marker.Provider})");
            }
        }

        public void Dispose()
        {
            try { _libraryManager.ItemAdded -= OnItemAdded; } catch { }
            try { _cts?.Cancel(); } catch { }
        }
    }
}
