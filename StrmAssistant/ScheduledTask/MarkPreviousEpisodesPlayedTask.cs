using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>
    /// 标记前集已看（对应 PRO 的 Mark Previous Episodes Played）。
    /// PRO 在播放事件里实时处理；本实现按定时轮询，存在最长一个轮询周期的延迟。
    /// </summary>
    public class MarkPreviousEpisodesPlayedTask : IScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Resources.Culture);

        public string Key => "MarkPreviousEpisodesPlayedTask";

        public string Name => "标记前集已看";

        public string Description => "播放进度达到阈值后，把之前的剧集标记为已看";

        public bool IsHidden => true;

        public bool IsEnabled => true;

        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            var enabled = Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions?.UIFunctionOptions?
                .MarkPreviousEpisodesPlayed ?? false;
            if (!enabled) yield break;

            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromMinutes(2).Ticks
            };
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var ui = Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions?.UIFunctionOptions;
            if (ui == null || !ui.MarkPreviousEpisodesPlayed) return Task.CompletedTask;

            var threshold = ui.MarkPreviousEpisodesPlayedThreshold;
            if (threshold <= 0) threshold = 90;

            var libraryManager = Plugin.LibraryManager;
            var userManager = Plugin.UserManager;
            var userDataManager = Plugin.UserDataManager;

            if (libraryManager == null || userManager == null || userDataManager == null) return Task.CompletedTask;

            var marked = 0;
            var candidates = 0;
            var noRuntime = 0;

            foreach (var user in userManager.GetUsers(new MediaBrowser.Model.Querying.UserQuery()).Items)
            {
                if (cancellationToken.IsCancellationRequested) break;

                List<Episode> episodes;
                try
                {
                    episodes = libraryManager.GetItemList(new InternalItemsQuery
                    {
                        Recursive = true,
                        IncludeItemTypes = new[] { typeof(Episode).Name }
                    }).OfType<Episode>().ToList();
                }
                catch (Exception e)
                {
                    _logger.Info("MarkPrevious - enumerate episodes failed: " + e.Message);
                    continue;
                }

                foreach (var episode in episodes)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var userData = userDataManager.GetUserData(user, episode);
                        if (userData == null || userData.Played) continue;
                        if (userData.PlaybackPositionTicks <= 0) continue;

                        candidates++;

                        if (!EpisodeProgressPolicy.ReachedThreshold(userData.PlaybackPositionTicks,
                                episode.RunTimeTicks, threshold))
                        {
                            if (!episode.RunTimeTicks.HasValue) noRuntime++;
                            continue;
                        }

                        var series = episode.Series ?? libraryManager.GetItemById(episode.SeriesId) as Series;
                        if (series == null) continue;

                        var siblings = libraryManager.GetItemList(new InternalItemsQuery
                        {
                            Recursive = true,
                            IncludeItemTypes = new[] { typeof(Episode).Name },
                            AncestorIds = new[] { series.InternalId }
                        }).OfType<Episode>().ToList();

                        foreach (var sibling in siblings)
                        {
                            if (!EpisodeProgressPolicy.IsBefore(sibling.ParentIndexNumber, sibling.IndexNumber,
                                    episode.ParentIndexNumber, episode.IndexNumber)) continue;

                            var data = userDataManager.GetUserData(user, sibling);
                            if (data != null && data.Played) continue;

                            data.Played = true;
                            userDataManager.SaveUserData(user, sibling, data,
                                MediaBrowser.Model.Entities.UserDataSaveReason.TogglePlayed,
                                CancellationToken.None);
                            marked++;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.Info("MarkPrevious - item failed for " + episode.Name + ": " + e.Message);
                    }
                }
            }

            _logger.Info($"MarkPrevious - candidates: {candidates}, marked: {marked}, no runtime skipped: {noRuntime}");
            Common.TaskReporter.Report("标记前集已看", "前集标记完成", _logger);
                        return Task.CompletedTask;
        }
    }
}
