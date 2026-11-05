using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using StrmAssistant.Dto;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO「统一片头库」：按 UnifiedIntroDbProviders 顺序尝试外部片头库。
    /// 默认端点可用环境变量覆盖，便于本地联调：
    ///   STRM_INTRODB_BASE（默认 https://api.theintrodb.org/v1）
    /// </summary>
    public class IntroDbApi
    {
        private static string BaseUrl =>
            Environment.GetEnvironmentVariable("STRM_INTRODB_BASE") is { Length: > 0 } custom
                ? custom.TrimEnd('/')
                : "https://api.theintrodb.org/v1";

        private readonly IHttpClient _httpClient;

        public IntroDbApi(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<IntroMarker> GetMarkerAsync(string imdbId, int season, int episode,
            CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(imdbId)) return null;

            var options = Plugin.Instance.GetPluginOptions().IntroSkipOptions;
            var providers = (options.UnifiedIntroDbProviders ?? "IntroDb,Tidb")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            foreach (var provider in providers)
            {
                // Tidb 用独立的 key（对应设置里的 TidbApiKey），其它提供方用公共 key
                var apiKey = string.Equals(provider, "Tidb", StringComparison.OrdinalIgnoreCase)
                    ? (string.IsNullOrWhiteSpace(options.TidbApiKey) ? options.IntroDbApiKey : options.TidbApiKey)
                    : options.IntroDbApiKey;

                var url = $"{BaseUrl}/media?imdb_id={imdbId}&season={season}&episode={episode}&provider={provider}" +
                          $"&apikey={apiKey}";

                var json = await GetRawAsync(url, token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(json)) continue;

                try
                {
                    var response = System.Text.Json.JsonSerializer.Deserialize<IntroDbMarkerResponse>(json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    var segments = response?.Segments ?? response?.Intro;
                    if (segments == null || segments.Count == 0) continue;

                    var intro = segments.FirstOrDefault(s =>
                        string.Equals(s.Type, "intro", StringComparison.OrdinalIgnoreCase)) ?? segments.First();
                    var credits = segments.FirstOrDefault(s =>
                        string.Equals(s.Type, "credits", StringComparison.OrdinalIgnoreCase)) ??
                                  response?.Credits?.FirstOrDefault();

                    return new IntroMarker
                    {
                        Provider = provider,
                        IntroStart = intro.Start > 0 ? intro.Start : intro.StartMs / 1000,
                        IntroEnd = intro.End > 0 ? intro.End : intro.EndMs / 1000,
                        CreditsStart = credits == null
                            ? 0
                            : (credits.Start > 0 ? credits.Start : credits.StartMs / 1000)
                    };
                }
                catch
                {
                    // 该提供方返回异常格式，继续下一个
                }
            }

            return null;
        }

        private async Task<string> GetRawAsync(string url, CancellationToken token)
        {
            for (var attempt = 1; attempt <= RetryPolicy.MaxAttempts; attempt++)
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

                    using var response = await _httpClient.SendAsync(options, "GET").ConfigureAwait(false);

                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        await using var stream = response.Content;
                        using var reader = new System.IO.StreamReader(stream);
                        return await reader.ReadToEndAsync().ConfigureAwait(false);
                    }

                    var status = (int)response.StatusCode;
                    if (!RetryPolicy.ShouldRetryStatus(status)) return null;

                    Plugin.Instance.Logger.Info(
                        $"IntroDb - retry {attempt}/{RetryPolicy.MaxAttempts} after HTTP {status}");
                }
                catch (Exception e) when (RetryPolicy.ShouldRetryException(e))
                {
                    if (attempt >= RetryPolicy.MaxAttempts)
                    {
                        Plugin.Instance.Logger.Info(
                            "IntroDb - giving up after " + RetryPolicy.MaxAttempts + " attempts: " +
                            e.GetBaseException().Message);
                        return null;
                    }

                    Plugin.Instance.Logger.Info(
                        $"IntroDb - retry {attempt}/{RetryPolicy.MaxAttempts} after " +
                        e.GetBaseException().Message);
                }

                if (attempt < RetryPolicy.MaxAttempts)
                {
                    try { await Task.Delay(RetryPolicy.DelayAfterAttempt(attempt), token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return null; }
                }
            }

            Plugin.Instance.Logger.Info("IntroDb - giving up after " + RetryPolicy.MaxAttempts + " attempts: " + url);
            return null;
        }
    }
}
