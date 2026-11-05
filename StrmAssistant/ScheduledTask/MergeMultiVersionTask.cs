using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Options.ExperienceEnhanceOptions;

namespace StrmAssistant.ScheduledTask
{
    public class TriggerMergeMovieTask : ILibraryPostScanTask
    {
        private readonly MergeMultiVersionTask _task;
        
        public TriggerMergeMovieTask(MergeMultiVersionTask task) => _task = task;

        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.MergeMultiVersion
                ? _task.Execute(cancellationToken, progress)
                : Task.CompletedTask;
        }
    }

    public class MergeMultiVersionTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;

        private static readonly HashSet<string> ProviderIdCheckKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tmdb", "imdb", "tvdb" };

        /// <summary>刷新后 key 仍不变、合并不了的剧集（id → 当时的 key），进程内有效。</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, string> UnmergeableSeries =
            new System.Collections.Concurrent.ConcurrentDictionary<long, string>();

        public static readonly AsyncLocal<CollectionFolder> CurrentScanLibrary = new AsyncLocal<CollectionFolder>();

        public MergeMultiVersionTask(ILibraryManager libraryManager, IProviderManager providerManager)
        {
            _logger = Plugin.Instance.Logger;
            _libraryManager = libraryManager;
            _providerManager = providerManager;
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("MergeMultiVersion - Scheduled Task Execute");

            var currentScanLibrary = CurrentScanLibrary.Value;
            CurrentScanLibrary.Value = null;

            double cumulativeProgress = 0;

            var seriesLibraryGroups = PrepareMergeSeries();
            var movieLibraryGroups = PrepareMergeMovies(currentScanLibrary);

            var processSeries = seriesLibraryGroups.Any() && (currentScanLibrary is null ||
                                                              currentScanLibrary.CollectionType == CollectionType.TvShows.ToString() ||
                                                              currentScanLibrary.CollectionType is null);
            var processMovies = movieLibraryGroups.Any() && (currentScanLibrary is null ||
                                                             currentScanLibrary.CollectionType == CollectionType.Movies.ToString() ||
                                                             currentScanLibrary.CollectionType is null);

            if (processSeries)
            {
                var multiply = processMovies ? 1 : 2;

                var alternativeSeries = FindDuplicateSeries(seriesLibraryGroups);
                progress.Report(cumulativeProgress += 5.0 * multiply);

                if (alternativeSeries.Any())
                {
                    var seriesProgressWeight = 35.0 * multiply / alternativeSeries.Count;

                    foreach (var series in alternativeSeries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var keyBefore = series.PresentationUniqueKey;

                        // 刷过一次 key 没变的（比如一边只有 tvdb id、另一边只有 tmdb id，key 来源不同），
                        // 再刷也合不了：本次运行期间不再对它做递归刷新，避免每次扫库都全量刷一遍
                        if (UnmergeableSeries.TryGetValue(series.InternalId, out var stuckKey) &&
                            string.Equals(stuckKey, keyBefore, StringComparison.Ordinal))
                        {
                            cumulativeProgress += seriesProgressWeight;
                            progress.Report(cumulativeProgress);
                            continue;
                        }

                        if (currentScanLibrary is null || _libraryManager.GetCollectionFolders(series)
                                .Any(c => c.InternalId != currentScanLibrary.InternalId))
                        {
                            var refreshOptions = Plugin.MetadataApi.GetMetadataValidationRefreshOptions();
                            // 递归：季和分集的 SeriesPresentationUniqueKey 也要跟着换，否则合并后的剧集在下次扫库前显示 0 季 0 集
                            EmbyCompat.SetIfExists(refreshOptions, "Recursive", true); // 4.8 没有这个属性

                            await _providerManager.RefreshFullItem(series, refreshOptions, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        cumulativeProgress += seriesProgressWeight;
                        progress.Report(cumulativeProgress);

                        var keyAfter = _libraryManager.GetItemById(series.InternalId)?.PresentationUniqueKey;
                        if (string.Equals(keyAfter, keyBefore, StringComparison.Ordinal))
                        {
                            UnmergeableSeries[series.InternalId] = keyBefore;
                            _logger.Info($"MergeMultiVersion - Series not merged (key unchanged, provider ids differ?): {series.Name} - {series.Path}");
                        }
                        else
                        {
                            _logger.Info($"MergeMultiVersion - Series merged: {series.Name} - {series.Path}");
                        }
                    }
                }
                else
                {
                    cumulativeProgress += 35.0 * multiply;
                    progress.Report(cumulativeProgress);
                }

                var inconsistentSeries = FindInconsistentSeries(seriesLibraryGroups);
                progress.Report(cumulativeProgress += 5.0 * multiply);

                if (inconsistentSeries.Any())
                {
                    var seriesProgressWeight = 5.0 * multiply / inconsistentSeries.Count;

                    foreach (var series in inconsistentSeries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var refreshOptions = Plugin.MetadataApi.GetMetadataValidationRefreshOptions();
                        // 递归：季和分集的 SeriesPresentationUniqueKey 也要跟着换，否则合并后的剧集在下次扫库前显示 0 季 0 集
                        EmbyCompat.SetIfExists(refreshOptions, "Recursive", true); // 4.8 没有这个属性

                        await _providerManager.RefreshFullItem(series, refreshOptions, cancellationToken)
                            .ConfigureAwait(false);

                        cumulativeProgress += seriesProgressWeight;
                        progress.Report(cumulativeProgress);

                        _logger.Info($"MergeMultiVersion - Series merged: {series.Name} - {series.Path}");
                    }
                }
                else
                {
                    cumulativeProgress += 5.0 * multiply;
                    progress.Report(cumulativeProgress);
                }
            }

            if (processMovies)
            {
                var totalGroups = movieLibraryGroups.Length;
                var groupProgressWeight = processSeries ? 50.0 / totalGroups : 100.0 / totalGroups;

                foreach (var group in movieLibraryGroups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // p 是本组内的累计进度（0–100），不是增量：以前直接累加，10 个组时会报到 650%
                    var groupBase = cumulativeProgress;
                    var groupProgress = new Progress<double>(p =>
                        progress.Report(Math.Min(100, groupBase + p * groupProgressWeight / 100)));

                    ExecuteMergeMovies(group, groupProgress);
                    cumulativeProgress = groupBase + groupProgressWeight;
                }
            }

            progress.Report(100);
            _logger.Info("MergeMultiVersion - Scheduled Task Complete");
            Common.TaskReporter.Report("合并多版本", "多版本合并完成", _logger);
        }

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Plugin.Instance.DefaultUICulture);

        public string Key => "MergeMultiVersionTask";

        public string Description => Resources.ResourceManager.GetString(
            "MergeMovieTask_Description_Merge_movies_per_library_or_across_libraries_per_preference",
            Plugin.Instance.DefaultUICulture);

        public string Name => "\u5408\u5e76\u591a\u7248\u672c";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

        public bool IsHidden => false;

        public bool IsEnabled => true;
        
        public bool IsLogged => true;

        private long[] PrepareMergeSeries()
        {
            var mergeSeriesPreference = Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.MergeSeriesPreference;
            _logger.Info("MergeMultiVersion - Merge Series Preference: " + mergeSeriesPreference);

            if (!Common.Hooks.SeriesMergePatch.Installed || mergeSeriesPreference != MergeSeriesScopeOption.GlobalScope)
                return Array.Empty<long>();

            var libraries = Plugin.LibraryApi.GetSeriesLibraries()
                .Where(l => l.GetLibraryOptions().EnableAutomaticSeriesGrouping)
                .ToList();

            if (!libraries.Any()) return Array.Empty<long>();

            _logger.Info("MergeMultiVersion - Series Libraries: " + string.Join(", ", libraries.Select(l => l.Name)));

            var libraryGroups = libraries.Select(l => l.InternalId).ToArray();

            return libraryGroups;
        }

        private List<BaseItem> FindDuplicateSeries(long[] parents)
        {
            var allSeries = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    ParentIds = parents,
                    IncludeItemTypes = new[] { nameof(Series) },
                    HasAnyProviderId = new[]
                    {
                        MetadataProviders.Tmdb.ToString(), MetadataProviders.Imdb.ToString(),
                        MetadataProviders.Tvdb.ToString()
                    }
                })
                .ToList();

            var dupSeries = allSeries
                .SelectMany(item =>
                    item.ProviderIds.Where(kvp => ProviderIdCheckKeys.Contains(kvp.Key))
                        .Select(kvp => new { kvp.Key, kvp.Value, item }))
                .GroupBy(x => new { x.Key, x.Value })
                .Where(g =>
                {
                    var uniqueKeys = new HashSet<string>();
                    foreach (var x in g)
                    {
                        uniqueKeys.Add(x.item.PresentationUniqueKey);
                        if (uniqueKeys.Count > 1) return true;
                    }

                    return false;
                })
                .SelectMany(g => g.Select(x => x.item))
                .GroupBy(item => item.InternalId)
                .Select(g => g.First())
                .ToList();

            return dupSeries;
        }

        private List<Series> FindInconsistentSeries(long[] parents)
        {
            var allSeries = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    ParentIds = parents,
                    IncludeItemTypes = new[] { nameof(Season), nameof(Episode) },
                    GroupBySeriesPresentationUniqueKey = true
                })
                .ToList();

            var inconsistentSeries = allSeries
                .Select(item => new
                {
                    Series = (item as Season)?.Series ?? (item as Episode)?.Series, item.SeriesPresentationUniqueKey
                })
                .Where(x => x.Series != null && x.SeriesPresentationUniqueKey != x.Series.PresentationUniqueKey)
                .GroupBy(x => x.Series.InternalId)
                .Select(g => g.First().Series)
                .ToList();

            allSeries.Clear();
            allSeries.TrimExcess();

            return inconsistentSeries;
        }

        private long[][] PrepareMergeMovies(CollectionFolder currentScanLibrary)
        {
            var mergeMoviesPreference = Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.MergeMoviesPreference;
            _logger.Info("MergeMultiVersion - Merge Movies Preference: " + mergeMoviesPreference);

            var libraryGroups = Array.Empty<long[]>();

            if (mergeMoviesPreference == MergeMoviesScopeOption.LibraryScope && currentScanLibrary != null)
            {
                libraryGroups = new[] { new[] { currentScanLibrary.InternalId } };
                _logger.Info("MergeMultiVersion - Movies Libraries: " + currentScanLibrary.Name);
            }
            else
            {
                var libraries = Plugin.LibraryApi.GetMovieLibraries();

                if (!libraries.Any()) return libraryGroups;

                _logger.Info(
                    "MergeMultiVersion - Movies Libraries: " + string.Join(", ", libraries.Select(l => l.Name)));

                var libraryIds = libraries.Select(l => l.InternalId).ToArray();
                libraryGroups = mergeMoviesPreference == MergeMoviesScopeOption.GlobalScope
                    ? new[] { libraryIds }
                    : libraryIds.Select(library => new[] { library }).ToArray();
            }

            return libraryGroups;
        }

        private void ExecuteMergeMovies(long[] parents, IProgress<double> groupProgress = null)
        {
            var allMovies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                ParentIds = parents,
                IncludeItemTypes = new[] { nameof(Movie) },
                HasAnyProviderId = new[]
                {
                    MetadataProviders.Tmdb.ToString(),
                    MetadataProviders.Imdb.ToString(),
                    MetadataProviders.Tvdb.ToString()
                }
            }).Cast<Movie>().ToList();

            _logger.Info("MergeMultiVersion - 候选电影(有 provider id): " + allMovies.Count);

            var dupMovies = allMovies
                .SelectMany(item =>
                    item.ProviderIds.Where(kvp => ProviderIdCheckKeys.Contains(kvp.Key))
                        .Select(kvp => new { kvp.Key, kvp.Value, item }))
                .GroupBy(kvp => new { kvp.Key, kvp.Value })
                .Where(g =>
                {
                    var groupItems = g.Select(kvp => kvp.item).ToList();

                    var altVersionCount = g.Sum(kvp =>
                        kvp.item.GetAlternateVersionIds().Count(id => groupItems.Any(i => i.InternalId == id)));

                    return g.Count() != 1 + altVersionCount / g.Count();
                })
                .ToList();
            allMovies.Clear();
            allMovies.TrimExcess();

            _logger.Info("MergeMultiVersion - 重复组: " + dupMovies.Count);

            if (dupMovies.Count > 0)
            {
                var parentMap = new Dictionary<long, long>(dupMovies.Count);

                foreach (var group in dupMovies)
                {
                    long rootId = -1;

                    foreach (var kvp in group)
                    {
                        var movie = kvp.item;

                        if (!parentMap.ContainsKey(movie.InternalId))
                        {
                            parentMap[movie.InternalId] = movie.InternalId;
                        }

                        if (rootId == -1)
                            rootId = movie.InternalId;
                        else
                            Union(rootId, movie.InternalId, parentMap);
                    }
                }

                var rootIdGroups = parentMap.Values.GroupBy(id => Find(id, parentMap)).ToList();

                var movieLookup = dupMovies.SelectMany(g => g)
                    .GroupBy(kvp => Find(kvp.item.InternalId, parentMap))
                    .ToDictionary(d => d.Key,
                        d => d.GroupBy(kvp => kvp.item.InternalId).Select(g => g.First().item).ToList());

                var total = rootIdGroups.Count;
                var current = 0;

                foreach (var group in rootIdGroups)
                {
                    var movies = group
                        .SelectMany(
                            rootId => movieLookup.TryGetValue(rootId, out var m) ? m : Enumerable.Empty<Movie>())
                        .GroupBy(m => m.InternalId)
                        .Select(g => g.First())
                        .OfType<BaseItem>()
                        .ToArray();

                    if (Common.DryRunMode.Skip("合并多版本 " + string.Join(" / ", movies.Select(m => m.Name + " - " + m.Path)), _logger))
                    {
                        current++;
                        continue;
                    }

                    _libraryManager.MergeItems(movies);

                    foreach (var item in movies)
                    {
                        _logger.Info($"MergeMultiVersion - Movie merged: {item.Name} - {item.Path}");
                    }

                    current++;
                    _logger.Info($"MergeMultiVersion - Merged group {current} of {total} with {movies.Length} items");

                    var progress = (double)current / total * 100;
                    groupProgress?.Report(progress);
                }

                groupProgress?.Report(100);
            }
        }
    }
}
