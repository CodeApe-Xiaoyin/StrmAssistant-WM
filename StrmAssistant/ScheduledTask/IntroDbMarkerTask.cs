using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>
    /// 移植自 PRO：批量用外部片头库补全缺失的片头/片尾标记。
    /// </summary>
    public class IntroDbMarkerTask : IScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Resources.Culture);

        public string Key => "IntroDbMarkerTask";

        public string Name => "片头标记补全";

        public string Description => "用外部片头库为缺少片头标记的剧集补全章节";

        public bool IsHidden => false;

        public bool IsEnabled => true;

        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // 对应 PRO 的「启用自动刮削」：PRO 在扫描时同步执行，这里改为定时轮询
            var enabled = Plugin.Instance?.GetPluginOptions()?.IntroSkipOptions?.EnableAutomaticIntroScraping ?? false;
            if (!enabled) yield break;

            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(6).Ticks
            };
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = Plugin.Instance.GetPluginOptions().IntroSkipOptions;
            _logger.Info("IntroDbMarker - Scheduled Task Execute");
            _logger.Info("IntroDb providers: " + options.UnifiedIntroDbProviders);

            var libraryManager = Plugin.LibraryManager;
            var itemRepository = Plugin.ItemRepository;

            if (libraryManager == null || itemRepository == null)
            {
                _logger.Info("IntroDbMarker - services unavailable");
                return Task.CompletedTask;
            }

            // 没有配置 IntroDb API Key 时直接跳过：
            // 否则会对每个分集发请求、每个都 404 重试 3 次，白白消耗外网与时间（生产实测）
            var introDbKey = Plugin.Instance.GetPluginOptions().IntroSkipOptions.IntroDbApiKey;
            if (string.IsNullOrWhiteSpace(introDbKey))
            {
                _logger.Info("IntroDbMarker - IntroDbApiKey 未配置，跳过");
                Common.TaskReporter.Report("片头标记补全", "跳过：未配置 IntroDb API Key", _logger);
                return Task.CompletedTask;
            }

            var query = new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { typeof(Episode).Name }
            };

            var episodes = libraryManager.GetItemList(query).OfType<Episode>().ToList();
            _logger.Info("IntroDbMarker - episodes found: " + episodes.Count);

            var api = new IntroDbApi(Plugin.HttpClient);
            var applied = 0;

            for (var i = 0; i < episodes.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var episode = episodes[i];
                progress?.Report(100.0 * i / Math.Max(1, episodes.Count));

                var series = episode.Series;
                if (series == null) continue;

                var existing = itemRepository.GetChapters(episode, cancellationToken);
                _logger.Debug($"IntroDbMarker - {episode.Name} existing chapters: {existing.Count}, " +
                            $"markers: {string.Join(",", existing.Select(c => c.MarkerType.ToString()))}");

                if (existing.Any(c => c.MarkerType == MediaBrowser.Model.Entities.MarkerType.IntroStart))
                    continue;

                if (!series.ProviderIds.TryGetValue("Imdb", out var imdbId) || string.IsNullOrEmpty(imdbId))
                    continue;

                var marker = api.GetMarkerAsync(imdbId, episode.ParentIndexNumber ?? 0, episode.IndexNumber ?? 0,
                    cancellationToken).GetAwaiter().GetResult();

                if (marker == null) continue;

                if (!IntroDbApplyServiceHelper.Apply(itemRepository, episode, marker)) continue;
                applied++;
                _logger.Info($"IntroDbMarker - applied to {episode.Name} ({marker.Provider})");
            }

            _logger.Info("IntroDbMarker - applied markers: " + applied);
            Common.TaskReporter.Report("片头标记补全", "已完成外部片头库匹配", _logger);
            return Task.CompletedTask;
        }
    }
}
