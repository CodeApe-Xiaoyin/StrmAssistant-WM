using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using StrmAssistant.Dto;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO 的豆瓣数据访问层（frodo API）。
    /// BaseUrl 默认指向豆瓣；可用环境变量 STRM_DOUBAN_BASE 覆盖，便于本地联调。
    /// </summary>
    public class DoubanApi
    {
        private const string DefaultBaseUrl = "https://frodo.douban.com/api/v2";

        private static string BaseUrl =>
            Environment.GetEnvironmentVariable("STRM_DOUBAN_BASE") is { Length: > 0 } custom
                ? custom.TrimEnd('/')
                : DefaultBaseUrl;

        /// <summary>豆瓣 apiKey：默认留空，可用环境变量 STRM_DOUBAN_APIKEY 注入。</summary>
        private static string ApiKey =>
            Environment.GetEnvironmentVariable("STRM_DOUBAN_APIKEY") ?? string.Empty;

        /// <summary>用户在设置里填的 Cookie / AccessToken（用于需要登录态的豆瓣接口）。</summary>
        private static string Cookie =>
            Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions?.DoubanCookie ?? string.Empty;

        private static string AccessToken =>
            Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions?.DoubanAccessToken ?? string.Empty;

        private readonly IHttpClient _httpClient;

        public DoubanApi(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }


        public async Task<DoubanSearchResponse> SearchAsync(string query, string type, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            var url = $"{BaseUrl}/search/weixin?q={System.Uri.EscapeDataString(query)}&apiKey={ApiKey}";
            var json = await GetWithRetryAsync(url, token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json)) return null;

            return JsonSerializer.Deserialize<DoubanSearchResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        private static void ApplyAuthHeaders(HttpRequestOptions options)
        {
            var cookie = Cookie;
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                options.RequestHeaders["Cookie"] = cookie;
            }

            var accessToken = AccessToken;
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                options.RequestHeaders["Authorization"] = "Bearer " + accessToken;
            }
        }

        private const int MaxAttempts = RetryPolicy.MaxAttempts;

        private static async Task DelayBeforeRetryAsync(int attempt, CancellationToken token)
        {
            try { await Task.Delay(RetryPolicy.DelayAfterAttempt(attempt), token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        private async Task<string> GetWithRetryAsync(string url, CancellationToken token)
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    var options = new HttpRequestOptions
                    {
                        Url = url,
                        CancellationToken = token,
                        AcceptHeader = "application/json",
                        BufferContent = true,
                        UserAgent = Plugin.Instance.UserAgent
                    };

                    ApplyAuthHeaders(options);

                    if (ScrapingHttpClient.ProxyEnabled)
                    {
                        using var clientResponse = await ScrapingHttpClient.Get()
                            .GetAsync(url, token).ConfigureAwait(false);

                        if (!clientResponse.IsSuccessStatusCode)
                        {
                            var code = (int)clientResponse.StatusCode;
                            if (RetryPolicy.ShouldRetryStatus(code))
                            {
                                Plugin.Instance.Logger.Info(
                                    $"Douban - retry {attempt}/{MaxAttempts} after HTTP {code}");
                                await DelayBeforeRetryAsync(attempt, token).ConfigureAwait(false);
                                continue;
                            }

                            return null;
                        }

                        return await clientResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }

                    using var response = await _httpClient.SendAsync(options, "GET").ConfigureAwait(false);

                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        await using var stream = response.Content;
                        using var reader = new System.IO.StreamReader(stream);
                        return await reader.ReadToEndAsync().ConfigureAwait(false);
                    }

                    var status = (int)response.StatusCode;
                    if (RetryPolicy.ShouldRetryStatus(status))
                    {
                        Plugin.Instance.Logger.Info(
                            $"Douban - retry {attempt}/{MaxAttempts} after HTTP {status}");
                        await DelayBeforeRetryAsync(attempt, token).ConfigureAwait(false);
                        continue;
                    }

                    return null;
                }
                catch (Exception e) when (RetryPolicy.ShouldRetryException(e))
                {
                    // Emby 的 IHttpClient 对 5xx 会直接抛异常，这里统一按可重试处理
                    if (attempt >= MaxAttempts)
                    {
                        Plugin.Instance.Logger.Info(
                            "Douban - giving up after " + MaxAttempts + " attempts: " +
                            e.GetBaseException().Message);
                        return null;
                    }

                    Plugin.Instance.Logger.Info(
                        $"Douban - retry {attempt}/{MaxAttempts} after {e.GetBaseException().Message}");
                    await DelayBeforeRetryAsync(attempt, token).ConfigureAwait(false);
                }
            }

            Plugin.Instance.Logger.Info("Douban - giving up after " + MaxAttempts + " attempts: " + url);
            return null;
        }

        private async Task<string> GetRawAsync(string url, CancellationToken token)
        {
            var options = new HttpRequestOptions
            {
                Url = url,
                CancellationToken = token,
                AcceptHeader = "application/json",
                BufferContent = true,
                UserAgent = Plugin.Instance.UserAgent
            };

            if (ScrapingHttpClient.ProxyEnabled)
            {
                using var clientResponse = await ScrapingHttpClient.Get()
                    .GetAsync(url, token).ConfigureAwait(false);
                return await clientResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            using var response = await _httpClient.SendAsync(options, "GET").ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.OK) return null;

            await using var stream = response.Content;
            using var reader = new System.IO.StreamReader(stream);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        public async Task<DoubanInfo> GetInfoAsync(string doubanId, string type, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(doubanId)) return null;

            var mediaType = string.IsNullOrWhiteSpace(type) ? "movie" : type;
            var url = $"{BaseUrl}/{mediaType}/{doubanId}?apiKey={ApiKey}";
            var json = await GetWithRetryAsync(url, token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json)) return null;

            return JsonSerializer.Deserialize<DoubanInfo>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }
}
