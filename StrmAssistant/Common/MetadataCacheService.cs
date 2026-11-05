using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO「构建本地元数据缓存」：把条目元数据写成 nfo + 海报，落到本地缓存目录。
    /// 目录结构：&lt;根目录&gt;/&lt;条目所在库名&gt;/&lt;条目相对路径&gt;/
    /// </summary>
    public enum CacheWriteResult
    {
        Written,
        Skipped,
        Failed
    }

    public class MetadataCacheService
    {
        private readonly ILogger _logger;

        public MetadataCacheService(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<CacheWriteResult> WriteAsync(BaseItem item, string rootFolder, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || string.IsNullOrWhiteSpace(item.Path))
                return CacheWriteResult.Failed;

            if (DryRunMode.Enabled)
            {
                _logger.Info("MetadataCache - dry run, skip writing for " + item.Name);
                return CacheWriteResult.Skipped;
            }

            try
            {
                var targetDir = BuildTargetDirectory(item, rootFolder);
                Directory.CreateDirectory(targetDir);

                var nfoName = item is Series ? "tvshow.nfo" : "movie.nfo";
                var nfoPath = Path.Combine(targetDir, nfoName);

                // 增量：缓存文件比条目修改时间新时跳过该条的 nfo 写入
                var seriesWritten = false;
                if (!IsCacheFresh(nfoPath, item))
                {
                    await File.WriteAllTextAsync(nfoPath, BuildNfo(item), Encoding.UTF8, token)
                        .ConfigureAwait(false);
                    seriesWritten = true;
                }

                // 剧集额外写分集 nfo（<根>/<剧名>/Season xx/<集名>.nfo）
                var episodesWritten = false;
                if (item is Series series)
                {
                    episodesWritten = await WriteEpisodesAsync(series, targetDir, token).ConfigureAwait(false);
                }

                // 条目实例未必带 ImageInfos，逐级回退取主图路径
                var posterPath = item.PrimaryImagePath;

                if (string.IsNullOrEmpty(posterPath))
                {
                    posterPath = item.GetImagePath(ImageType.Primary, 0);
                }

                if (string.IsNullOrEmpty(posterPath))
                {
                    posterPath = item.ImageInfos?.FirstOrDefault(i => i.Type == ImageType.Primary)?.Path;
                }


                if (!string.IsNullOrEmpty(posterPath) && File.Exists(posterPath))
                {
                    var posterTarget = Path.Combine(targetDir, "poster" + Path.GetExtension(posterPath));
                    File.Copy(posterPath, posterTarget, true);
                }

                return seriesWritten || episodesWritten
                    ? CacheWriteResult.Written
                    : CacheWriteResult.Skipped;
            }
            catch (Exception e)
            {
                _logger.Info("MetadataCache - failed for " + item.Name + ": " + e.Message);
                return CacheWriteResult.Failed;
            }
        }

        internal static bool IsCacheFresh(string path, BaseItem item)
        {
            if (!File.Exists(path)) return false;
            return File.GetLastWriteTimeUtc(path) >= item.DateLastSaved.UtcDateTime;
        }

        private async Task<bool> WriteEpisodesAsync(Series series, string targetDir, CancellationToken token)
        {
            var anyWritten = false;

            try
            {
                var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = new[] { typeof(Episode).Name }
                };
                query.AncestorIds = new[] { series.InternalId };

                var episodes = Plugin.LibraryManager?.GetItemList(query)
                    .OfType<Episode>()
                    .OrderBy(e => e.ParentIndexNumber)
                    .ThenBy(e => e.IndexNumber)
                    .ToList();

                if (episodes == null) return false;

                foreach (var episode in episodes)
                {
                    if (token.IsCancellationRequested) break;

                    var seasonDir = Path.Combine(targetDir,
                        "Season " + (episode.ParentIndexNumber ?? 0).ToString("00"));
                    Directory.CreateDirectory(seasonDir);

                    var fileName = Sanitize(series.Name + " S" +
                                            (episode.ParentIndexNumber ?? 0).ToString("00") + "E" +
                                            (episode.IndexNumber ?? 0).ToString("00")) + ".nfo";

                    var episodePath = Path.Combine(seasonDir, fileName);
                    if (IsCacheFresh(episodePath, episode)) continue;

                    await File.WriteAllTextAsync(episodePath, BuildEpisodeNfo(episode, series), Encoding.UTF8, token)
                        .ConfigureAwait(false);
                    anyWritten = true;
                }
            }
            catch (Exception e)
            {
                _logger.Info("MetadataCache - episodes failed for " + series.Name + ": " + e.Message);
                return false;
            }

            return anyWritten;
        }

        internal static string BuildEpisodeNfo(Episode episode, Series series)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<episodedetails>");
            sb.AppendLine("  <title>" + SecurityElement.Escape(episode.Name ?? string.Empty) + "</title>");
            sb.AppendLine("  <season>" + (episode.ParentIndexNumber ?? 0) + "</season>");
            sb.AppendLine("  <episode>" + (episode.IndexNumber ?? 0) + "</episode>");

            if (!string.IsNullOrWhiteSpace(episode.Overview))
                sb.AppendLine("  <plot>" + SecurityElement.Escape(episode.Overview) + "</plot>");

            if (episode.PremiereDate.HasValue)
                sb.AppendLine("  <aired>" + episode.PremiereDate.Value.ToString("yyyy-MM-dd") + "</aired>");

            if (episode.CommunityRating.HasValue)
                sb.AppendLine("  <rating>" + episode.CommunityRating.Value.ToString("0.0") + "</rating>");

            if (!string.IsNullOrWhiteSpace(series.Name))
                sb.AppendLine("  <showtitle>" + SecurityElement.Escape(series.Name) + "</showtitle>");

            sb.AppendLine("</episodedetails>");
            return sb.ToString();
        }

        /// <summary>
        /// 电影 / 剧集的缓存目录名：「标题 (年份)」。
        /// 以前只用标题，《狮子王》1994 和 2019 共用一个目录，后写的覆盖先写的，恢复时还会把别的片子的元数据写进来。
        /// </summary>
        internal static string CacheFolderName(BaseItem item)
        {
            var year = item.ProductionYear;
            return Sanitize(year.HasValue && year.Value > 0 ? item.Name + " (" + year.Value + ")" : item.Name);
        }

        private static string BuildTargetDirectory(BaseItem item, string rootFolder)
        {
            var name = Sanitize(item.Name);
            if (item is Series || item is Movie)
            {
                return Path.Combine(rootFolder, CacheFolderName(item));
            }

            var parent = item.GetParent();
            var parentName = parent == null ? "unknown" : Sanitize(parent.Name);
            return Path.Combine(rootFolder, parentName, name);
        }

        /// <summary>单个路径名的字节上限（Linux 255 字节，留出扩展名余量）。</summary>
        internal const int MaxNameBytes = 200;

        internal static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unknown";

            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                sb.Append(Array.IndexOf(InvalidChars, ch) >= 0 ? '_' : ch);
            }

            var sanitized = sb.ToString().Trim();

            // 中文名按 UTF-8 一字 3 字节，长标题很容易超过文件系统上限（255 字节），
            // 这里按字节截断并补短哈希，保证可读且不冲突。
            return TruncateByUtf8Bytes(sanitized, MaxNameBytes);
        }

        internal static string TruncateByUtf8Bytes(string value, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var encoding = new UTF8Encoding(false);
            if (encoding.GetByteCount(value) <= maxBytes) return value;

            var hash = BitConverter.ToString(
                    System.Security.Cryptography.SHA1.Create()
                        .ComputeHash(encoding.GetBytes(value)))
                .Replace("-", string.Empty)
                .Substring(0, 8)
                .ToLowerInvariant();

            var suffix = "~" + hash;
            var budget = maxBytes - encoding.GetByteCount(suffix);

            var sb = new StringBuilder();
            var used = 0;

            foreach (var ch in value)
            {
                var len = encoding.GetByteCount(ch.ToString());
                if (used + len > budget) break;
                sb.Append(ch);
                used += len;
            }

            return sb + suffix;
        }

        /// <summary>
        /// 统一的非法字符集合（按 Windows 规则）。
        /// 不用 Path.GetInvalidFileNameChars()：它在 Linux 上只包含 '/'，
        /// 会导致同一部剧在 Windows 与 Linux 上生成出不同的缓存路径。
        /// </summary>
        internal static readonly char[] InvalidChars =
        {
            '\\', '/', ':', '*', '?', '"', '<', '>', '|'
        };


        internal static string BuildNfo(BaseItem item)
        {
            var sb = new StringBuilder();
            var root = item is Series ? "tvshow" : "movie";
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<" + root + ">");
            sb.AppendLine("  <title>" + SecurityElement.Escape(item.Name ?? string.Empty) + "</title>");

            if (!string.IsNullOrWhiteSpace(item.Overview))
                sb.AppendLine("  <plot>" + SecurityElement.Escape(item.Overview) + "</plot>");

            if (item.ProductionYear.HasValue)
                sb.AppendLine("  <year>" + item.ProductionYear.Value + "</year>");

            if (item.PremiereDate.HasValue)
                sb.AppendLine("  <premiered>" + item.PremiereDate.Value.ToString("yyyy-MM-dd") + "</premiered>");

            if (item.CommunityRating.HasValue)
                sb.AppendLine("  <rating>" + item.CommunityRating.Value.ToString("0.0") + "</rating>");

            foreach (var id in item.ProviderIds.Where(i => !string.IsNullOrWhiteSpace(i.Value)))
            {
                var tag = id.Key.ToLowerInvariant();
                if (tag != "tmdb" && tag != "imdb" && tag != "tvdb" && tag != "douban") continue;
                sb.AppendLine("  <" + tag + "id>" + SecurityElement.Escape(id.Value) + "</" + tag + "id>");
            }

            sb.AppendLine("</" + root + ">");
            return sb.ToString();
        }
    }
}
