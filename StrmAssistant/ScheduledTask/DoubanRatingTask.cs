using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>移植自 PRO：按回溯天数刷新豆瓣评分。</summary>
    public class DoubanRatingTask : IScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Resources.Culture);

        public string Key => "DoubanRatingTask";

        public string Name => "豆瓣评分更新";

        public string Description => "按首播日期回溯刷新豆瓣评分";

        public bool IsHidden => false;

        public bool IsEnabled => true;

        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // 每天 05:00 刷新回溯窗口内的豆瓣评分
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = TimeSpan.FromHours(5).Ticks
            };
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            _logger.Info("DoubanRating - Scheduled Task Execute");
            _logger.Info("Lookback days: " + options.RatingUpdateLookbackDays);

            // 只在开了「豆瓣辅助刮削」时更新评分：以前不管开没开都每天跑，
            // 条目上有豆瓣 id（可能是别的插件写的）就会被逐条请求豆瓣、覆盖评分。
            if (!options.EnableDoubanAssistScraping)
            {
                _logger.Info("DoubanRating - 未开启「豆瓣辅助刮削」，跳过");
                progress.Report(100);
                return Task.CompletedTask;
            }

            var libraryManager = Plugin.LibraryManager;
            if (libraryManager == null) return Task.CompletedTask;

            var items = libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { typeof(Series).Name, typeof(Movie).Name }
            });

            _logger.Info("DoubanRating - items found: " + System.Linq.Enumerable.Count(items));

            var service = new DoubanRatingService(libraryManager, _logger);
            var updated = 0;
            var skipped = 0;

            foreach (var entity in items)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var item = libraryManager.GetItemById(entity.Id) ?? entity;

                    if (!item.ProviderIds.TryGetValue("Douban", out var doubanId) || string.IsNullOrEmpty(doubanId))
                        continue;

                    if (!DoubanRatingService.WithinLookback(item, options.RatingUpdateLookbackDays))
                    {
                        skipped++;
                        _logger.Info("DoubanRating - skipped (out of lookback): " + item.Name);
                        continue;
                    }

                    if (service.ApplyAsync(item, doubanId, item is Series ? "tv" : "movie", cancellationToken)
                        .GetAwaiter().GetResult())
                    {
                        updated++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info("DoubanRating - item failed for " + entity.Name + ": " + ex.Message);
                }
            }

            _logger.Info($"DoubanRating - updated: {updated}, skipped: {skipped}");
            Common.TaskReporter.Report("豆瓣评分", "豆瓣评分更新完成", _logger);
                        return Task.CompletedTask;
        }
    }
}
