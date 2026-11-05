using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 从本地元数据缓存还原条目信息（缓存优先，属于显式操作）。
    /// </summary>
    public class MetadataCacheRestoreService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public MetadataCacheRestoreService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        /// <summary>条目没有主图时，用缓存里的 poster 还原（缓存优先）。</summary>
        private void RestorePoster(BaseItem item, string dir)
        {
            try
            {
                var hasPrimary = item.ImageInfos != null &&
                                 item.ImageInfos.Any(i => i.Type == ImageType.Primary);
                if (hasPrimary) return;

                var poster = Directory.GetFiles(dir, "poster.*").FirstOrDefault();
                if (poster == null) return;

                var providerManager = Plugin.ProviderManager;
                var fileSystem = Plugin.FileSystem;
                if (providerManager == null || fileSystem == null) return;

                var directoryService = new DirectoryService(_logger, fileSystem);
                var libraryOptions = _libraryManager.GetLibraryOptions(item);

                if (DryRunMode.Skip("还原海报 " + item?.Name, _logger)) return;

                providerManager.SaveImage(item, libraryOptions, poster, ImageType.Primary, null,
                    Array.Empty<long>(), directoryService, false, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();

                _libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.ImageUpdate);

                _logger.Info("Metadata cache restored poster for " + item.Name);
            }
            catch (Exception e)
            {
                _logger.Info("Metadata cache poster restore failed for " + item.Name + ": " + e.Message);
            }
        }

        public bool Restore(BaseItem item, string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || string.IsNullOrWhiteSpace(item.Name)) return false;

            var dir = ResolveCacheDirectory(item, rootFolder);
            if (dir == null) return false;

            var nfo = Path.Combine(dir, item is Series ? "tvshow.nfo" : "movie.nfo");
            if (!File.Exists(nfo)) return false;

            try
            {
                var doc = TextFile.LoadXml(nfo);
                var root = doc.Root;
                if (root == null) return false;

                var changed = false;

                var title = root.Element("title")?.Value;
                if (!string.IsNullOrWhiteSpace(title) && !string.Equals(item.Name, title, StringComparison.Ordinal))
                {
                    item.Name = title;
                    changed = true;
                }

                var plot = root.Element("plot")?.Value;
                if (!string.IsNullOrWhiteSpace(plot) &&
                    !string.Equals(item.Overview, plot, StringComparison.Ordinal))
                {
                    item.Overview = plot;
                    changed = true;
                }

                var year = root.Element("year")?.Value;
                if (int.TryParse(year, out var y) && item.ProductionYear != y)
                {
                    item.ProductionYear = y;
                    changed = true;
                }

                var rating = root.Element("rating")?.Value;
                if (float.TryParse(rating, out var r) && Math.Abs((item.CommunityRating ?? 0) - r) > 0.01f)
                {
                    item.CommunityRating = r;
                    changed = true;
                }

                RestorePoster(item, dir);

                if (changed)
                {
                    if (DryRunMode.Skip("从缓存还原 " + item.Name, _logger)) return true;

                    _libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit);
                    _logger.Info("Metadata cache restored for " + item.Name);
                }

                var episodesChanged = false;
                if (item is Series series) episodesChanged = RestoreEpisodes(series, dir);

                return changed || episodesChanged;
            }
            catch (Exception e)
            {
                _logger.Info("Metadata cache restore failed for " + item.Name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 定位缓存目录，并确认目录里的 nfo 确实属于这个条目：
        ///   1) 新目录名「标题 (年份)」；2) 旧目录名「标题」；3) 按 provider id 在整个缓存里找（条目改过名）。
        /// 1、2 找到的目录也要核对 provider id / 年份，对不上就不用——同名不同片（如两部《狮子王》）不能互相覆盖。
        /// </summary>
        private string ResolveCacheDirectory(BaseItem item, string rootFolder)
        {
            var nfoName = item is Series ? "tvshow.nfo" : "movie.nfo";

            foreach (var name in new[] { MetadataCacheService.CacheFolderName(item), Sanitize(item.Name) }.Distinct())
            {
                var dir = Path.Combine(rootFolder, name);
                var nfo = Path.Combine(dir, nfoName);
                if (File.Exists(nfo) && NfoBelongsTo(item, nfo)) return dir;
            }

            return FindByProviderId(item, rootFolder, nfoName);
        }

        private static readonly (string tag, string provider)[] IdTags =
        {
            ("tmdbid", "Tmdb"), ("imdbid", "Imdb"), ("tvdbid", "Tvdb"), ("doubanid", "Douban")
        };

        /// <summary>nfo 是不是这个条目的：有可比的 id 就按 id；没有就比年份；都没有当作是（兼容旧缓存）。</summary>
        internal static bool NfoBelongsTo(BaseItem item, string nfoPath)
        {
            try
            {
                var root = TextFile.LoadXml(nfoPath).Root;
                if (root == null) return false;

                var compared = false;
                foreach (var (tag, provider) in IdTags)
                {
                    var cached = root.Element(tag)?.Value;
                    if (string.IsNullOrWhiteSpace(cached)) continue;
                    if (!item.ProviderIds.TryGetValue(provider, out var current) || string.IsNullOrWhiteSpace(current)) continue;

                    if (string.Equals(current.Trim(), cached.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                    compared = true;
                }

                if (compared) return false;

                var year = root.Element("year")?.Value;
                if (!string.IsNullOrWhiteSpace(year) && item.ProductionYear.HasValue)
                    return string.Equals(year.Trim(), item.ProductionYear.Value.ToString(), StringComparison.Ordinal);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // provider id → 缓存目录，整个任务只扫一遍缓存（以前每个找不到目录的条目都把所有缓存目录解析一遍）
        private Dictionary<string, string> _idIndex;
        private string _idIndexRoot;

        private string FindByProviderId(BaseItem item, string rootFolder, string nfoName)
        {
            if (_idIndex == null || !string.Equals(_idIndexRoot, rootFolder, StringComparison.Ordinal))
            {
                _idIndexRoot = rootFolder;
                _idIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var candidate in Directory.GetDirectories(rootFolder))
                    {
                        foreach (var n in new[] { "tvshow.nfo", "movie.nfo" })
                        {
                            var nfo = Path.Combine(candidate, n);
                            if (!File.Exists(nfo)) continue;
                            try
                            {
                                var root = TextFile.LoadXml(nfo).Root;
                                if (root == null) continue;
                                foreach (var (tag, provider) in IdTags)
                                {
                                    var value = root.Element(tag)?.Value;
                                    if (string.IsNullOrWhiteSpace(value)) continue;
                                    var key = n + "|" + provider + "|" + value.Trim();
                                    if (!_idIndex.ContainsKey(key)) _idIndex[key] = candidate;
                                }
                            }
                            catch
                            {
                                // 坏掉的 nfo 跳过
                            }
                        }
                    }
                }
                catch
                {
                    // 扫描失败则视为找不到
                }
            }

            foreach (var (_, provider) in IdTags)
            {
                if (item.ProviderIds.TryGetValue(provider, out var id) && !string.IsNullOrWhiteSpace(id) &&
                    _idIndex.TryGetValue(nfoName + "|" + provider + "|" + id.Trim(), out var dir))
                    return dir;
            }

            return null;
        }

        private bool RestoreEpisodes(Series series, string seriesDir)
        {
            var anyChanged = false;

            try
            {
                var query = new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { typeof(Episode).Name }
                };
                query.AncestorIds = new[] { series.InternalId };

                var episodes = _libraryManager.GetItemList(query).OfType<Episode>().ToList();

                // 分集 nfo 的文件名前缀用的是缓存目录名（可能已与当前剧名不同），不能用 series.Name
                var seriesFolderName = Path.GetFileName(seriesDir.TrimEnd(Path.DirectorySeparatorChar));


                foreach (var episode in episodes)
                {
                    var seasonDir = Path.Combine(seriesDir,
                        "Season " + (episode.ParentIndexNumber ?? 0).ToString("00"));
                    var file = Path.Combine(seasonDir,
                        seriesFolderName + " S" + (episode.ParentIndexNumber ?? 0).ToString("00") + "E" +
                        (episode.IndexNumber ?? 0).ToString("00") + ".nfo");

                    if (!File.Exists(file)) continue;

                    var doc = TextFile.LoadXml(file);
                    var root = doc.Root;
                    if (root == null) continue;

                    var changed = false;
                    var title = root.Element("title")?.Value;
                    if (!string.IsNullOrWhiteSpace(title) && !string.Equals(episode.Name, title, StringComparison.Ordinal))
                    {
                        episode.Name = title;
                        changed = true;
                    }

                    var plot = root.Element("plot")?.Value;
                    if (!string.IsNullOrWhiteSpace(plot) &&
                        !string.Equals(episode.Overview, plot, StringComparison.Ordinal))
                    {
                        episode.Overview = plot;
                        changed = true;
                    }

                    if (changed)
                    {
                        if (!DryRunMode.Skip("从缓存还原分集 " + episode.Name, _logger))
                            _libraryManager.UpdateItem(episode, series, ItemUpdateType.MetadataEdit);
                        anyChanged = true;
                        _logger.Info("Metadata cache restored for " + episode.Name);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Info("Metadata cache episode restore failed: " + e.Message);
            }

            return anyChanged;
        }

        internal static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unknown";

            // 直接用写缓存时的同一个函数（以前这里少了按字节截断，长标题写和读的目录名对不上）
            return MetadataCacheService.Sanitize(name);
        }
    }
}
