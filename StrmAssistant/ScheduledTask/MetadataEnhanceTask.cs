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
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>
    /// 移植自 PRO 的元数据增强任务：
    /// 按开关把「剧集组」和「豆瓣」的数据应用到库里已有 TMDb / 豆瓣 ID 的条目。
    /// </summary>
    public class MetadataEnhanceTask : IScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Resources.Culture);

        public string Key => "MetadataEnhanceTask";

        public string Name => "元数据增强";

        public string Description => "按开关注入剧集组与豆瓣数据";

        public bool IsHidden => false;

        public bool IsEnabled => true;

        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();


        /// <summary>对应 PRO：条目没有剧集组时，从 TMDb 剧集组列表里自动挑一个（取集数最多的）。</summary>
        private string AutoSelectEpisodeGroup(string tmdbId)
        {
            try
            {
                var service = new StrmAssistant.Web.Service.MovieDbEpisodeGroupService();
                var result = service.Get(new StrmAssistant.Web.Api.GetMovieDbEpisodeGroups { TmdbId = tmdbId })
                    .GetAwaiter().GetResult() as StrmAssistant.Dto.MovieDbEpisodeGroupList;

                var picked = result?.Results?
                    .Where(r => !string.IsNullOrEmpty(r.Id))
                    .OrderByDescending(r => r.EpisodeCount)
                    .ThenBy(r => r.Id)
                    .FirstOrDefault();

                return picked?.Id;
            }
            catch (Exception e)
            {
                _logger.Info("Episode group auto-select failed: " + e.Message);
                return null;
            }
        }


        /// <summary>对应 PRO：条目没有豆瓣 ID 时按标题搜索并取首个匹配。</summary>
        private string ResolveDoubanId(string name, string type)
        {
            try
            {
                var httpClient = Plugin.HttpClient;
                if (httpClient == null) return null;

                var api = new Common.DoubanApi(httpClient);

                // 全角括号、年份等会让搜索命中率变差，先归一化
                var keyword = Common.SearchNameNormalizer.Normalize(name);
                if (string.IsNullOrWhiteSpace(keyword)) keyword = name;

                var result = api.SearchAsync(keyword, type, CancellationToken.None).GetAwaiter().GetResult();
                return result?.Items?.FirstOrDefault(i => i.Target != null && !string.IsNullOrEmpty(i.Target.Id))
                    ?.Target.Id;
            }
            catch (Exception e)
            {
                _logger.Info("Douban search failed: " + e.Message);
                return null;
            }
        }

        /// <summary>豆瓣辅助刮削范围：ALL / Movie / Series（空值按 ALL）。</summary>
        private static bool IsInDoubanScope(string scope, bool isSeries)
        {
            if (string.IsNullOrWhiteSpace(scope) ||
                string.Equals(scope, "ALL", StringComparison.OrdinalIgnoreCase)) return true;

            if (string.Equals(scope, "Movie", StringComparison.OrdinalIgnoreCase)) return !isSeries;
            if (string.Equals(scope, "Series", StringComparison.OrdinalIgnoreCase)) return isSeries;

            return true;
        }

        /// <summary>演练模式开启时跳过写库动作。</summary>
        private static void ApplyOrSkip(Action action)
        {
            if (Common.DryRunMode.Enabled) return;
            action();
        }

        /// <summary>与「处理哪些内容」相关的开关指纹；变化说明需要重新过一遍全部条目。</summary>
        internal static string ComputeOptionsFingerprint(StrmAssistant.Options.MetadataEnhanceOptions options)
        {
            if (options == null) return string.Empty;

            var parts = new[]
            {
                options.PinyinSortName, options.PreferTraditionalChinese, options.ClearTagline,
                options.ChineseMovieDb, options.ChineseTvdb, options.BlockNonFallbackLanguage,
                options.PreferOriginalPoster, options.MovieDbEpisodeGroup, options.LocalEpisodeGroup,
                options.EnableMovieDbEpisodeGroupExtendedInfo, options.EnhanceNfoMetadata,
                options.MetadataCacheMode, options.EnhanceMovieDbPerson, options.EnableDoubanAssistScraping
            };

            var raw = string.Join(",", parts.Select(p => p ? "1" : "0"));
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hash = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
            return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 16).ToLowerInvariant();
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            var mediaOptions = Plugin.Instance.GetPluginOptions().MediaInfoExtractOptions;

            _logger.Info("MetadataEnhance - Scheduled Task Execute");
            _logger.Info("MovieDb Episode Group Enabled: " + options.MovieDbEpisodeGroup);
            _logger.Info("Douban Assist Enabled: " + options.EnableDoubanAssistScraping);

            var libraryManager = Plugin.LibraryManager;

            if (libraryManager == null)
            {
                _logger.Info("MetadataEnhance - library manager unavailable");
                return Task.CompletedTask;
            }

            _logger.Info("MetadataEnhance - pinyin sort enabled: " + options.PinyinSortName);

            var seriesApplied = 0;
            var doubanApplied = 0;
            var sortNameApplied = 0;
            var traditionalApplied = 0;
            var taglineCleared = 0;
            var posterApplied = 0;
            var episodeNameApplied = 0;
            var nfoImagesApplied = 0;
            var personEnhanced = 0;
            var personSkipped = 0;
            var metadataCached = 0;
            var imageCaptured = 0;
            var metadataCacheSkipped = 0;
            var chineseApplied = 0;

            var seriesList = libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { typeof(Series).Name }
            });

            var movieList = libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { typeof(Movie).Name }
            });

            // 增量：仅处理自上次运行后变更过的条目（可用「每次都全量处理」关闭）
            var stateFile = System.IO.Path.Combine(
                Plugin.Instance.ApplicationPaths.PluginConfigurationsPath ?? System.IO.Path.GetTempPath(),
                "StrmAssistant-metadata-lastrun.txt");

            var lastRun = System.DateTimeOffset.MinValue;
            var previousFingerprint = string.Empty;

            try
            {
                if (System.IO.File.Exists(stateFile))
                {
                    // 状态文件两行：第一行时间戳，第二行「功能开关指纹」
                    var lines = System.IO.File.ReadAllLines(stateFile);
                    if (lines.Length > 0 && long.TryParse(lines[0].Trim(), out var ticks))
                        lastRun = new System.DateTimeOffset(ticks, System.TimeSpan.Zero);
                    if (lines.Length > 1) previousFingerprint = lines[1].Trim();
                }
            }
            catch
            {
                // 读取失败按全量处理
            }

            // 功能开关变了就必须全量跑一次，否则新开的功能会被增量逻辑整批跳过
            var fingerprint = ComputeOptionsFingerprint(options);
            var optionsChanged = previousFingerprint.Length > 0 &&
                                 !string.Equals(previousFingerprint, fingerprint, StringComparison.Ordinal);

            if (optionsChanged)
            {
                _logger.Info("MetadataEnhance - 检测到功能开关变化，本次按全量处理（避免新功能被增量跳过）");
            }

            var incremental = !options.FullScanOnEveryRun && !optionsChanged &&
                              lastRun > System.DateTimeOffset.MinValue;
            _logger.Info("MetadataEnhance - incremental: " + incremental +
                        (incremental ? " (since " + lastRun.ToString("u") + ")" : " (full scan)"));

            var unchangedSkipped = 0;
            var incrementalEnabledHintShown = false;

            _logger.Info("MetadataEnhance - series found: " + System.Linq.Enumerable.Count(seriesList) +
                        ", movies: " + System.Linq.Enumerable.Count(movieList));

            foreach (var entity in seriesList.Concat(movieList))
            {
                if (cancellationToken.IsCancellationRequested) break;

                var item = libraryManager.GetItemById(entity.Id) ?? entity;

                if (incremental && item.DateLastSaved <= lastRun.UtcDateTime)
                {
                    unchangedSkipped++;
                    continue;
                }
                var isSeries = item is Series;

                try
                {

                if (options.PinyinSortName && Common.SortNameHelper.ContainsChinese(item.Name))
                {
                    var sortName = Common.SortNameHelper.BuildSortName(item.Name);
                    if (!string.Equals(item.SortName, sortName, StringComparison.Ordinal))
                    {
                        item.SortName = sortName;
                        ApplyOrSkip(() => libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit));
                        sortNameApplied++;
                        _logger.Info("Sort name updated for " + item.Name + ": " + sortName);
                    }
                }

                if (options.PreferTraditionalChinese && !string.IsNullOrWhiteSpace(item.Overview))
                {
                    var traditional = Common.ChineseTextHelper.ToTraditional(item.Overview);
                    if (!string.Equals(traditional, item.Overview, StringComparison.Ordinal))
                    {
                        item.Overview = traditional;
                        ApplyOrSkip(() => libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit));
                        traditionalApplied++;
                        _logger.Info("Overview converted to traditional for " + item.Name);
                    }
                }

                if (options.ClearTagline)
                {
                    var taglinesProp = item.GetType().GetProperty("Taglines");
                    var taglines = taglinesProp?.GetValue(item) as System.Collections.IList;
                    if (taglines != null && taglines.Count > 0)
                    {
                        taglines.Clear();
                        ApplyOrSkip(() => libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit));
                        taglineCleared++;
                        _logger.Info("Taglines cleared for " + item.Name);
                    }
                }

                try
                {
                    if (options.ChineseMovieDb &&
                        item.ProviderIds.TryGetValue("Tmdb", out var zhTmdbId) && !string.IsNullOrEmpty(zhTmdbId))
                    {
                        var chineseService = new Common.PosterService(libraryManager, Plugin.ProviderManager,
                            Plugin.FileSystem, _logger);
                        if (chineseService.ApplyChineseMetadataAsync(item, zhTmdbId, isSeries,
                                options.FallbackLanguages, cancellationToken, options.BlockNonFallbackLanguage)
                            .GetAwaiter().GetResult())
                        {
                            ApplyOrSkip(() => libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit));
                            chineseApplied++;
                            _logger.Info("Chinese metadata applied to " + item.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataEnhance - feature chinese-movie failed for " + item.Name + ": " + ex.Message);
                }

                try
                {
                    // 分集名走的是 TMDB 接口，所以两个「中文化」开关任开其一即生效
                    if (isSeries && (options.ChineseMovieDb || options.ChineseTvdb) &&
                        item.ProviderIds.TryGetValue("Tmdb", out var epTmdbId) && !string.IsNullOrEmpty(epTmdbId))
                    {
                        var episodeQuery = new InternalItemsQuery
                        {
                            Recursive = true,
                            IncludeItemTypes = new[] { typeof(Episode).Name }
                        };
                        episodeQuery.AncestorIds = new[] { item.InternalId };

                        var episodeService = new Common.PosterService(libraryManager, Plugin.ProviderManager,
                            Plugin.FileSystem, _logger);

                        foreach (var ep in libraryManager.GetItemList(episodeQuery).OfType<Episode>())
                        {
                            if (ep.ParentIndexNumber == null || ep.IndexNumber == null) continue;
                            if (episodeService.ApplyChineseEpisodeAsync(ep, epTmdbId, ep.ParentIndexNumber.Value,
                                    ep.IndexNumber.Value, cancellationToken).GetAwaiter().GetResult())
                            {
                                ApplyOrSkip(() => libraryManager.UpdateItem(ep, item, ItemUpdateType.MetadataEdit));
                                episodeNameApplied++;
                                _logger.Info("Chinese episode name applied to " + ep.Name);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataEnhance - feature chinese-episode failed for " + item.Name + ": " + ex.Message);
                }

                try
                {
                    // 分集图片截取（没有主图的分集，用 ffmpeg 截一帧）
                    if (isSeries && mediaOptions?.EnableImageCapture == true)
                    {
                        var epQuery = new InternalItemsQuery
                        {
                            Recursive = true,
                            IncludeItemTypes = new[] { typeof(Episode).Name }
                        };
                        epQuery.AncestorIds = new[] { item.InternalId };

                        var captureService = new Common.ImageCaptureService(libraryManager, _logger);

                        foreach (var ep in libraryManager.GetItemList(epQuery).OfType<Episode>())
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            if (captureService.CaptureAsync(ep, mediaOptions.ImageCapturePosition, cancellationToken)
                                .GetAwaiter().GetResult())
                            {
                                imageCaptured++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataEnhance - feature image-capture failed for " + item.Name + ": " + ex.Message);
                }

                try
                {
                    if (mediaOptions?.EnableImageCapture == true)
                {
                    var capture = new Common.ImageCaptureService(libraryManager, _logger);
                    if (capture.CaptureAsync(item, mediaOptions.ImageCapturePosition, cancellationToken)
                        .GetAwaiter().GetResult())
                    {
                        imageCaptured++;
                    }
                }

                if (options.MetadataCacheMode && !string.IsNullOrWhiteSpace(options.MetadataCacheRootFolder) &&
                    (isSeries || item is Movie))
                {
                    var cacheService = new Common.MetadataCacheService(_logger);
                    switch (cacheService.WriteAsync(item, options.MetadataCacheRootFolder, cancellationToken)
                                .GetAwaiter().GetResult())
                    {
                        case Common.CacheWriteResult.Written:
                            metadataCached++;
                            _logger.Info("Metadata cache written for " + item.Name);
                            break;
                        case Common.CacheWriteResult.Skipped:
                            metadataCacheSkipped++;
                            break;
                    }
                }

                if (options.EnhanceNfoMetadata)
                    {
                        var nfoPath = Common.NfoService.ResolveNfoPath(item);
                        if (!string.IsNullOrEmpty(nfoPath))
                        {
                            var dataPath = Plugin.Instance.ApplicationPaths.DataPath;
                            var metadataRoot = System.IO.Path.Combine(
                                System.IO.Path.GetDirectoryName(dataPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)) ?? dataPath, "metadata");
                            var peopleRoot = System.IO.Path.Combine(metadataRoot, "People");
                            _logger.Info("NFO people root: " + peopleRoot);
                            var nfoService = new Common.NfoService(Plugin.ProviderManager, Plugin.FileSystem, _logger);
                            var n = nfoService.ApplyActorImagesAsync(nfoPath, peopleRoot, cancellationToken)
                                .GetAwaiter().GetResult();
                            if (n > 0)
                            {
                                nfoImagesApplied += n;
                                _logger.Info("NFO actor images applied for " + item.Name + ": " + n);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataEnhance - feature nfo failed for " + item.Name + ": " + ex.Message);
                }

                try
                {
                    if (options.PreferOriginalPoster &&
                        item.ProviderIds.TryGetValue("Tmdb", out var posterTmdbId) && !string.IsNullOrEmpty(posterTmdbId))
                    {
                        var posterService = new Common.PosterService(libraryManager, Plugin.ProviderManager,
                            Plugin.FileSystem, _logger);
                        if (posterService.ApplyOriginalPosterAsync(item, posterTmdbId, isSeries, cancellationToken)
                            .GetAwaiter().GetResult())
                        {
                            posterApplied++;
                            _logger.Info("Original poster applied to " + item.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataEnhance - feature poster failed for " + item.Name + ": " + ex.Message);
                }

                try
                {
                    if (isSeries && options.MovieDbEpisodeGroup)
                    {
                        var localPath = string.IsNullOrEmpty(item.Path)
                            ? null
                            : System.IO.Path.Combine(item.Path, "episodegroup.json");

                        if (options.LocalEpisodeGroup && localPath != null && System.IO.File.Exists(localPath))
                        {
                            var localMapping = StrmAssistant.Web.Service.ApplyEpisodeGroupService.LoadLocalMapping(localPath);
                            if (localMapping != null && localMapping.Count > 0)
                            {
                                new StrmAssistant.Web.Service.ApplyEpisodeGroupService(libraryManager)
                                    .ApplyMapping((Series)item, localMapping);
                                seriesApplied++;
                                _logger.Info("Episode group loaded from local json for " + item.Name);
                            }
                        }
                        else if (item.ProviderIds.TryGetValue("Tmdb", out var tmdbId) && !string.IsNullOrEmpty(tmdbId))
                        {
                            if (!item.ProviderIds.TryGetValue("TmdbEpisodeGroup", out var groupId) ||
                                string.IsNullOrEmpty(groupId))
                            {
                                groupId = AutoSelectEpisodeGroup(tmdbId);
                                if (!string.IsNullOrEmpty(groupId))
                                {
                                    item.ProviderIds["TmdbEpisodeGroup"] = groupId;
                                    ApplyOrSkip(() => libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit));
                                    _logger.Info("Episode group auto-selected for " + item.Name + ": " + groupId);
                                }
                            }

                            if (!string.IsNullOrEmpty(groupId))
                            {
                                var applyService = new StrmAssistant.Web.Service.ApplyEpisodeGroupService(libraryManager);
                                applyService.Post(new StrmAssistant.Web.Api.ApplyMovieDbEpisodeGroup
                                {
                                    SeriesId = item.Id.ToString(),
                                    TmdbId = tmdbId,
                                    GroupId = groupId
                                }).GetAwaiter().GetResult();
                                seriesApplied++;
                                _logger.Info("Episode group applied to " + item.Name + " (tmdb " + tmdbId + ", group " + groupId + ")");

                                if (options.LocalEpisodeGroup && localPath != null)
                                {
                                    try
                                    {
                                        var raw = Plugin.MetadataApi.GetMovieDbRawAsync(
                                            "https://api.themoviedb.org/3/tv/" + tmdbId + "/episode_group/" + groupId +
                                            "?api_key=" + CommonUtility.ResolveMovieDbApiKey() +
                                            "&language=zh-CN", cancellationToken).GetAwaiter().GetResult();
                                        if (!string.IsNullOrEmpty(raw))
                                        {
                                            System.IO.File.WriteAllText(localPath, raw);
                                            _logger.Info("Episode group stored locally: " + localPath);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        _logger.Info("Episode group local store failed: " + e.Message);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataEnhance - feature episode-group failed for " + item.Name + ": " + ex.Message);
                }

                try
                {
                    if (options.EnableDoubanAssistScraping && IsInDoubanScope(options.DoubanAssistScrapeScope, isSeries))
                    {
                        if (!item.ProviderIds.TryGetValue("Douban", out var doubanId) || string.IsNullOrEmpty(doubanId))
                        {
                            doubanId = ResolveDoubanId(item.Name, isSeries ? "tv" : "movie");
                            if (!string.IsNullOrEmpty(doubanId))
                            {
                                item.ProviderIds["Douban"] = doubanId;
                                ApplyOrSkip(() => libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit));
                                _logger.Info("Douban id resolved for " + item.Name + ": " + doubanId);
                            }
                        }

                        if (!string.IsNullOrEmpty(doubanId))
                        {
                            var httpClient = Plugin.HttpClient;
                            if (httpClient != null)
                            {
                                var doubanService = new StrmAssistant.Web.Service.DoubanApplyService(libraryManager, httpClient);
                                doubanService.Post(new StrmAssistant.Web.Api.ApplyDoubanToItem
                                {
                                    ItemId = item.Id.ToString(),
                                    DoubanId = doubanId,
                                    Type = isSeries ? "tv" : "movie"
                                }).GetAwaiter().GetResult();
                                doubanApplied++;
                                _logger.Info("Douban info applied to " + item.Name);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataEnhance - feature douban failed for " + item.Name + ": " + ex.Message);
                }
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataEnhance - item " + item.Name + " failed: " + ex.Message);
                }
            }

            if (options.EnhanceMovieDbPerson)
            {
                var personList = libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { typeof(Person).Name }
                });

                _logger.Info("MetadataEnhance - persons found: " + System.Linq.Enumerable.Count(personList));

                var personService = new Common.PersonEnhanceService(libraryManager, _logger);

                foreach (var p in personList)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var person = (libraryManager.GetItemById(p.Id) ?? p) as Person;
                        if (person == null) continue;
                        if (!person.ProviderIds.TryGetValue("Tmdb", out var personTmdbId) ||
                            string.IsNullOrEmpty(personTmdbId)) continue;

                        // 人物刷新策略：默认只刷新 30 天前、无头像、非成人
                        if (options.RefreshPersonPreferences ==
                            Options.MetadataEnhanceOptions.RefreshPersonPreference.Default)
                        {
                            var hasAvatar = person.ImageInfos != null &&
                                            System.Linq.Enumerable.Any(person.ImageInfos,
                                                i => i.Type == ImageType.Primary);
                            if (hasAvatar)
                            {
                                personSkipped++;
                                _logger.Info("Person skipped (has avatar): " + person.Name);
                                continue;
                            }

                            if (person.DateLastRefreshed > DateTimeOffset.UtcNow.AddDays(-30))
                            {
                                personSkipped++;
                                _logger.Info("Person skipped (refreshed recently): " + person.Name);
                                continue;
                            }
                        }

                        if (personService.ApplyAsync(person, personTmdbId, cancellationToken)
                            .GetAwaiter().GetResult())
                        {
                            personEnhanced++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Info("MetadataEnhance - feature person failed for " + p.Name + ": " + ex.Message);
                    }
                }
            }

            _logger.Info("MetadataEnhance - candidate series: {0}, candidate douban items: {1}, sort names: {2}, " +
                        "traditional: {3}, taglines cleared: {4}",
                seriesApplied, doubanApplied, sortNameApplied, traditionalApplied, taglineCleared);
            _logger.Info("MetadataEnhance - posters applied: " + posterApplied +
                        ", chinese metadata: " + chineseApplied + ", episode names: " + episodeNameApplied +
                        ", nfo actor images: " + nfoImagesApplied + ", persons enhanced: " + personEnhanced +
                        ", persons skipped: " + personSkipped);
            _logger.Info("MetadataEnhance - metadata cache written: " + metadataCached +
                        ", skipped: " + metadataCacheSkipped);
            if (unchangedSkipped > 0 && !incrementalEnabledHintShown && !options.FullScanOnEveryRun)
            {
                _logger.Info("MetadataEnhance - 提示：本次跳过了 " + unchangedSkipped +
                            " 个未变更条目。如果刚开启新功能并希望应用到全部条目，" +
                            "请勾选「每次都全量处理」后重跑。");
                incrementalEnabledHintShown = true;
            }

            _logger.Info("MetadataEnhance - unchanged items skipped: " + unchangedSkipped +
                        ", images captured: " + imageCaptured);

            // 演练模式、被取消的运行都不写状态文件：
            // 否则"练一遍"或"跑一半被打断"会把条目标记成已处理，下次反而被增量跳过
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.Info("MetadataEnhance - 任务被取消：不更新增量状态（下次会继续处理剩余条目）");
            }
            else if (Common.DryRunMode.Enabled)
            {
                _logger.Info("MetadataEnhance - 演练模式：不更新增量状态（下次仍会处理这些条目）");
            }
            else
            {
                try
                {
                    System.IO.File.WriteAllText(stateFile,
                        System.DateTimeOffset.UtcNow.UtcTicks + System.Environment.NewLine +
                        ComputeOptionsFingerprint(options));
                }
                catch
                {
                    // 状态写入失败只会导致下次全量，不影响本次结果
                }
            }

            Common.TaskReporter.Report("元数据增强",
                $"剧集 {seriesApplied} / 豆瓣 {doubanApplied} / 排序 {sortNameApplied} / 繁体 {traditionalApplied} / " +
                $"海报 {posterApplied} / 中文 {chineseApplied} / 分集名 {episodeNameApplied} / 人物 {personEnhanced} / " +
                $"缓存 {metadataCached}", _logger);

            return Task.CompletedTask;
        }
    }
}
