using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using StrmAssistant.Dto;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO「原语言海报」：按原始语言挑海报并写入条目。
    /// </summary>
    public class PosterService
    {
        private const string ImageBaseUrl = "https://image.tmdb.org/t/p/original";

        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        public PosterService(ILibraryManager libraryManager, IProviderManager providerManager,
            IFileSystem fileSystem, ILogger logger)
        {
            _libraryManager = libraryManager;
            _providerManager = providerManager;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public async Task<bool> ApplyOriginalPosterAsync(BaseItem item, string tmdbId, bool isTv,
            CancellationToken token)
        {
            if (string.IsNullOrEmpty(tmdbId)) return false;

            var apiKey = CommonUtility.ResolveMovieDbApiKey();
            var type = isTv ? "tv" : "movie";

            var detailJson = await Plugin.MetadataApi.GetMovieDbRawAsync(
                $"https://api.themoviedb.org/3/{type}/{tmdbId}?api_key={apiKey}&language=en", token)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(detailJson)) return false;

            var detail = System.Text.Json.JsonSerializer.Deserialize<TmdbTvDetail>(detailJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var language = detail?.OriginalLanguage;
            if (string.IsNullOrEmpty(language)) return false;

            var imagesJson = await Plugin.MetadataApi.GetMovieDbRawAsync(
                $"https://api.themoviedb.org/3/{type}/{tmdbId}/images?api_key={apiKey}&include_image_language=" +
                $"{language},en,null", token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(imagesJson)) return false;

            var images = System.Text.Json.JsonSerializer.Deserialize<TmdbImageList>(imagesJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var poster = images?.Posters?
                .Where(p => !string.IsNullOrEmpty(p.FilePath))
                .OrderByDescending(p => p.Language == language)
                .ThenByDescending(p => p.VoteAverage)
                .FirstOrDefault();

            if (poster == null) return false;

            var url = poster.FilePath.StartsWith("http")
                ? poster.FilePath
                : CommonUtility.ApplyAltMovieDbUrl(ImageBaseUrl + poster.FilePath);

            return await DownloadAndSaveAsync(item, url, token).ConfigureAwait(false);
        }


        /// <summary>对应 PRO「中文名优化」：按 zh-CN 重新取名称与简介并写入条目。</summary>
        public async Task<bool> ApplyChineseMetadataAsync(BaseItem item, string tmdbId, bool isTv,
            string fallbackLanguages, CancellationToken token, bool blockNonFallbackLanguage = false)
        {
            if (string.IsNullOrEmpty(tmdbId)) return false;

            var apiKey = CommonUtility.ResolveMovieDbApiKey();
            var type = isTv ? "tv" : "movie";

            var languages = string.IsNullOrWhiteSpace(fallbackLanguages)
                ? new[] { "zh-CN" }
                : fallbackLanguages.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var language in languages)
            {
                var json = await Plugin.MetadataApi.GetMovieDbRawAsync(
                    $"https://api.themoviedb.org/3/{type}/{tmdbId}?api_key={apiKey}&language={language}",
                    token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(json)) continue;

                var detail = System.Text.Json.JsonSerializer.Deserialize<TmdbLocalizedDetail>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var localizedName = detail?.Name ?? detail?.Title;
                if (string.IsNullOrWhiteSpace(localizedName)) continue;
                if (!Common.ChineseTextHelper.ContainsChinese(localizedName)) continue;

                var changed = false;
                if (!string.Equals(item.Name, localizedName, StringComparison.Ordinal))
                {
                    item.Name = localizedName;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(detail.Overview) &&
                    !string.Equals(item.Overview, detail.Overview, StringComparison.Ordinal))
                {
                    // 屏蔽非回退语言简介：目标语言是中文时，简介必须真的含中文才写入
                    var overviewAcceptable = !blockNonFallbackLanguage ||
                                             !language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                                             Common.ChineseTextHelper.ContainsChinese(detail.Overview);

                    if (overviewAcceptable)
                    {
                        item.Overview = detail.Overview;
                        changed = true;
                    }
                    else
                    {
                        _logger.Info("Overview rejected (non-fallback language) for " + item.Name);
                    }
                }

                return changed;
            }

            return false;
        }


        /// <summary>对应 PRO「中文剧集名」：按 zh-CN 取分集名称与简介并写入。</summary>
        public async Task<bool> ApplyChineseEpisodeAsync(BaseItem episode, string seriesTmdbId, int seasonNumber,
            int episodeNumber, CancellationToken token)
        {
            if (string.IsNullOrEmpty(seriesTmdbId) || seasonNumber <= 0 || episodeNumber <= 0) return false;

            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            var apiKey = CommonUtility.ResolveMovieDbApiKey();

            // 与其它 TMDB 调用保持一致：启用替代地址时走替代地址
            var baseUrl = options.AltMovieDbConfig && !string.IsNullOrWhiteSpace(options.AltMovieDbApiUrl)
                ? options.AltMovieDbApiUrl.TrimEnd('/')
                : "https://api.themoviedb.org";

            var url = $"{baseUrl}/3/tv/{seriesTmdbId}/season/{seasonNumber}/episode/{episodeNumber}" +
                      $"?api_key={apiKey}&language=zh-CN";

            var json = await Plugin.MetadataApi.GetMovieDbRawAsync(url, token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json)) return false;

            var detail = System.Text.Json.JsonSerializer.Deserialize<TmdbLocalizedDetail>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var localizedName = detail?.Name;
            if (string.IsNullOrWhiteSpace(localizedName) || !Common.ChineseTextHelper.ContainsChinese(localizedName))
                return false;

            var changed = false;
            if (!string.Equals(episode.Name, localizedName, StringComparison.Ordinal))
            {
                episode.Name = localizedName;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(detail.Overview) &&
                !string.Equals(episode.Overview, detail.Overview, StringComparison.Ordinal))
            {
                episode.Overview = detail.Overview;
                changed = true;
            }

            return changed;
        }

        private async Task<bool> DownloadAndSaveAsync(BaseItem item, string url, CancellationToken token)
        {
            var httpClient = Plugin.HttpClient;
            if (httpClient == null) return false;

            var options = new HttpRequestOptions
            {
                Url = url,
                CancellationToken = token,
                BufferContent = true,
                UserAgent = Plugin.Instance.UserAgent
            };

            using var response = await httpClient.SendAsync(options, "GET").ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.OK) return false;

            var tempPath = Path.Combine(Plugin.Instance.ApplicationPaths.TempDirectory,
                Guid.NewGuid() + ".jpg");

            await using (var stream = response.Content)
            {
                var bytes = new byte[stream.Length];
                await stream.ReadAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                await _fileSystem.WriteAllBytesAsync(tempPath, bytes, token).ConfigureAwait(false);
            }

            if (DryRunMode.Skip("写入海报 " + item?.Name, _logger))
            {
                try { _fileSystem.DeleteFile(tempPath); } catch { }
                return false;
            }

            var directoryService = new DirectoryService(_logger, _fileSystem);
            var libraryOptions = _libraryManager.GetLibraryOptions(item);

            await _providerManager.SaveImage(item, libraryOptions, tempPath, ImageType.Primary, null,
                Array.Empty<long>(), directoryService, false, token).ConfigureAwait(false);

            try { _fileSystem.DeleteFile(tempPath); } catch { }

            // SaveImage 只写文件，条目记录里的图片信息需要显式写回，否则界面/接口仍认为没有图
            _libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.ImageUpdate);

            return true;
        }
    }
}
