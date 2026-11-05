using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Options.MediaInfoExtractOptions;

namespace StrmAssistant.ScheduledTask
{
    public class ExtractIntroFingerprintTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;
        private readonly ITaskManager _taskManager;

        public ExtractIntroFingerprintTask(IFileSystem fileSystem, ITaskManager taskManager)
        {
            _logger = Plugin.Instance.Logger;
            _fileSystem = fileSystem;
            _taskManager = taskManager;
        }

        private static string[] KeyboardKeywordsOf(string keywords) => Common.KeywordFilter.Parse(keywords);

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("IntroFingerprintExtract - Scheduled Task Execute");

            var introOptions = Plugin.Instance.GetPluginOptions().IntroSkipOptions;
            // 「并行计算片头标记」只放宽本任务自己的并发，不改全局的二级并发数：
            // 以前直接替换并销毁全局信号量，用户设的二级并发（比如防网盘风控设成 1）被永久改成 CPU 核数，
            // 别的任务还在旧信号量上等着会永远卡住。
            var tier2Gate = QueueManager.Tier2Semaphore;
            if (introOptions.ParallelMarkerComputation)
            {
                var parallel = Math.Max(2, Environment.ProcessorCount);
                _logger.Info("IntroFingerprintExtract - Parallel marker computation enabled, concurrency for this run: " +
                             parallel);
                tier2Gate = new System.Threading.SemaphoreSlim(parallel);
            }


            
            var unlockIntroSkip = Plugin.Instance.GetPluginOptions().IntroSkipOptions.UnlockIntroSkip;
            if (!unlockIntroSkip)
            {
                progress.Report(100.0);
                _ = Plugin.NotificationApi.SendMessageToAdmins(
                    $"[{Resources.PluginOptions_EditorTitle_Strm_Assistant}] {Resources.IntroDetectionEnhancedNotEnabled}",
                    10000);
                _logger.Warn("Built-in Intro Detection Enhanced is not enabled.");
                _logger.Warn("IntroFingerprintExtract - Scheduled Task Aborted");
                Common.TaskReporter.Report("提取片头声纹", "任务中止：未开启内建片头检测增强", _logger);
                return;
            }

            var maxConcurrentCount = Plugin.Instance.GetPluginOptions().GeneralOptions.MaxConcurrentCount;
            _logger.Info("Master Max Concurrent Count: " + maxConcurrentCount);
            var cooldownSeconds = maxConcurrentCount == 1
                ? Plugin.Instance.GetPluginOptions().GeneralOptions.CooldownDurationSeconds
                : (int?)null;
            if (cooldownSeconds.HasValue) _logger.Info("Cooldown Duration Seconds: " + cooldownSeconds.Value);

            var persistMediaInfoMode = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions.PersistMediaInfoMode;
            _logger.Info("Persist MediaInfo Mode: " + persistMediaInfoMode);
            var persistMediaInfo = persistMediaInfoMode != PersistMediaInfoOption.None.ToString();
            var mediaInfoRestoreMode = persistMediaInfoMode == PersistMediaInfoOption.Restore.ToString();

            var preExtractEpisodes = Plugin.FingerprintApi.FetchIntroPreExtractTaskItems();
            var postExtractEpisodes = Plugin.FingerprintApi.FetchIntroFingerprintTaskItems();
            var episodes= preExtractEpisodes.Concat(postExtractEpisodes).ToList();

            // 「指纹排除关键词」：命中剧集名或路径的直接跳过
            var excludeKeywords = KeyboardKeywordsOf(introOptions.FingerprintExcludeKeywords);

            if (excludeKeywords.Length > 0)
            {
                var beforeCount = episodes.Count;
                episodes = episodes
                    .Where(e => !Common.KeywordFilter.Hit(e.Name, e.Path, introOptions.FingerprintExcludeKeywords))
                    .ToList();
                _logger.Info($"IntroFingerprintExtract - 关键词排除 {beforeCount - episodes.Count} 个剧集: {string.Join(",", excludeKeywords)}");
            }
            var groupedBySeason = episodes.GroupBy(e => e.Season).ToList();
            var seasonTasks = new List<Task>();

            double totalSeasons = groupedBySeason.Count;
            double totalEpisodes = episodes.Count;

            _logger.Info($"IntroFingerprintExtract - Number of seasons: {totalSeasons}");
            _logger.Info($"IntroFingerprintExtract - Number of episodes: {totalEpisodes}");

            if (totalEpisodes > 0) IsRunning = true;

            var directoryService = new DirectoryService(_logger, _fileSystem);

            var episodeIndex = 0;
            var seasonIndex = 0;
            var processedEpisodes = 0;
            var processedSeasons = 0;
            var episodeSkipCount = 0;
            var seasonSkipCount = 0;
            var episodeWeight = !mediaInfoRestoreMode ? 0.8 : 1.0;
            var seasonWeight = !mediaInfoRestoreMode ? 0.2 : 0.0;
            var seasonProgressMap = new ConcurrentDictionary<Season, double>();

            foreach (var season in groupedBySeason)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                    return;
                }

                var episodeTasks = new List<Task>();
                var seasonSkip = mediaInfoRestoreMode;

                foreach (var episode in season)
                {
                    var taskEpisode = episode;

                    var masterGate = QueueManager.MasterSemaphore; // 等和放用同一个实例（并发数中途可能被改）
                    try
                    {
                        await masterGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        masterGate.Release();
                        _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                        return;
                    }

                    var taskEpisodeIndex = ++episodeIndex;
                    var task = Task.Run(async () =>
                    {
                        bool? result1 = null;
                        Tuple<string, bool> result2 = null;

                        try
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                                return;
                            }

                            var deserializeResult = false;

                            if (!Plugin.LibraryApi.HasMediaInfo(taskEpisode))
                            {
                                result1 = await Plugin.LibraryApi
                                    .OrchestrateMediaInfoProcessAsync(taskEpisode, "IntroFingerprintExtract Task",
                                        cancellationToken).ConfigureAwait(false);

                                if (result1 is null)
                                {
                                    if (!mediaInfoRestoreMode)
                                    {
                                        _logger.Info(
                                            $"IntroFingerprintExtract - Episode skipped or non-existent: {taskEpisode.Name} - {taskEpisode.Path}");
                                    }
                                    
                                    Interlocked.Increment(ref episodeSkipCount);
                                    seasonSkip = true;
                                    return;
                                }
                            }
                            else if (persistMediaInfo)
                            {
                                deserializeResult = await Plugin.MediaInfoApi.DeserializeIntroMarker(taskEpisode,
                                    directoryService, "IntroFingerprintExtract Task").ConfigureAwait(false);
                            }

                            if (!deserializeResult && !Plugin.ChapterApi.HasIntro(taskEpisode))
                            {
                                if (!mediaInfoRestoreMode)
                                {
                                    result2 = await Plugin.FingerprintApi
                                        .CreateTitleFingerprint(taskEpisode, directoryService, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                else
                                {
                                    Interlocked.Increment(ref episodeSkipCount);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.Info(
                                $"IntroFingerprintExtract - Episode cancelled: {taskEpisode.Name} - {taskEpisode.Path}");
                        }
                        catch (Exception e)
                        {
                            _logger.Error(
                                $"IntroFingerprintExtract - Episode failed: {taskEpisode.Name} - {taskEpisode.Path}");
                            _logger.Error(e.Message);
                            _logger.Debug(e.StackTrace);
                        }
                        finally
                        {
                            if ((result1 is true || result2?.Item2 is true) && cooldownSeconds.HasValue)
                            {
                                try
                                {
                                    await Task.Delay(cooldownSeconds.Value * 1000, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch
                                {
                                    // ignored
                                }
                            }

                            masterGate.Release();

                            var currentCount = Interlocked.Increment(ref processedEpisodes);

                            var totalSeasonFraction = seasonProgressMap.Values.Sum();
                            var currentProgress = episodeWeight * currentCount / totalEpisodes +
                                                  seasonWeight * (processedSeasons + totalSeasonFraction) /
                                                  totalSeasons;
                            progress.Report(currentProgress * 100);

                            if (!mediaInfoRestoreMode)
                            {
                                _logger.Info(
                                    $"IntroFingerprintExtract - Episode Progress {currentCount}/{totalEpisodes} - Task {taskEpisodeIndex}: {taskEpisode.Path}");
                            }
                        }
                    }, cancellationToken);
                    episodeTasks.Add(task);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                    return;
                }

                var taskSeason = season.Key;

                var seasonProgress = new Progress<double>(fraction =>
                {
                    seasonProgressMap[taskSeason] = fraction;
                    var totalSeasonFraction = seasonProgressMap.Values.Sum();

                    var currentProgress = episodeWeight * processedEpisodes / totalEpisodes +
                                          seasonWeight * (processedSeasons + totalSeasonFraction) / totalSeasons;
                    progress.Report(currentProgress * 100);
                });

                var taskSeasonIndex = ++seasonIndex;
                var seasonTask = Task.Run(async () =>
                {
                    await Task.WhenAll(episodeTasks).ConfigureAwait(false);

                    if (seasonSkip)
                    {
                        if (!mediaInfoRestoreMode)
                        {
                            _logger.Info(
                                $"IntroFingerprintExtract - Season Skipped: {taskSeason.Name} - {taskSeason.Path}");
                        }

                        Interlocked.Increment(ref seasonSkipCount);
                        return;
                    }

                    try
                    {
                        await tier2Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        tier2Gate.Release();
                        _logger.Info("IntroFingerprintExtract - Scheduled Task Cancelled");
                        return;
                    }

                    try
                    {
                        await Plugin.FingerprintApi
                            .UpdateIntroMarkerForSeason(taskSeason, cancellationToken, seasonProgress)
                            .ConfigureAwait(false);

                        // 新算出的片头/片尾标记只写进了数据库；同步刷新 JSON，否则删库重加后标记无法还原。
                        if (persistMediaInfo && !mediaInfoRestoreMode)
                        {
                            await Plugin.MediaInfoApi
                                .PersistIntroMarkersForSeason(taskSeason, "IntroFingerprint Marker Update")
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.Info("IntroFingerprintExtract - Season cancelled: " + taskSeason.Name + " - " +
                                     taskSeason.Path);
                    }
                    catch (Exception e)
                    {
                        _logger.Error("IntroFingerprintExtract - Season failed: " + taskSeason.Name + " - " +
                                      taskSeason.Path);
                        _logger.Error(e.Message);
                        _logger.Debug(e.StackTrace);
                    }
                    finally
                    {
                        tier2Gate.Release();

                        seasonProgressMap.TryRemove(taskSeason, out _);
                        var currentCount = Interlocked.Increment(ref processedSeasons);

                        if (!mediaInfoRestoreMode)
                        {
                            _logger.Info(
                                $"IntroFingerprintExtract - Season Progress {currentCount}/{totalSeasons} - Task {taskSeasonIndex}: {taskSeason.Path}");
                        }
                    }
                }, cancellationToken);
                seasonTasks.Add(seasonTask);
            }

            await Task.WhenAll(seasonTasks).ConfigureAwait(false);

            if (episodes.Count > 0) IsRunning = false;

            progress.Report(100.0);
            _logger.Info($"IntroFingerprintExtract - Number of seasons skipped: {seasonSkipCount}");
            _logger.Info($"IntroFingerprintExtract - Number of episodes skipped: {episodeSkipCount}");
            _logger.Info("IntroFingerprintExtract - Scheduled Task Complete");
            Common.TaskReporter.Report("提取片头声纹", "片头声纹提取完成", _logger);
        }

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Plugin.Instance.DefaultUICulture);

        public string Key => "IntroFingerprintExtractTask";

        public string Description => Resources.ResourceManager.GetString(
            "ExtractIntroFingerprintTask_Description_Extracts_intro_fingerprint_from_episodes",
            Plugin.Instance.DefaultUICulture);

        public string Name =>
            Resources.ResourceManager.GetString("ExtractIntroFingerprintTask_Name_Extract_Intro_Fingerprint",
                Plugin.Instance.DefaultUICulture);

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        public static bool IsRunning { get; private set; }
    }
}
