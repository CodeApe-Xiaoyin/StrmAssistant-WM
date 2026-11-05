using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// MDBList 评分源（对应 PRO 的 MdbListApiKey）。
    /// 有 imdb/tmdb id 的条目可以去 MDBList 拿综合评分，写入条目的社区评分。
    /// </summary>
    public static class MdbListApi
    {
        // 允许测试时替换（默认官方地址）
        public static string BaseUrl { get; set; } = "https://api.mdblist.com";

        public sealed class Result
        {
            public bool Ok { get; set; }
            public double? Score { get; set; }
            public int? Votes { get; set; }
            public string Error { get; set; }
            public int StatusCode { get; set; }
        }

        public static async Task<Result> GetRatingAsync(string imdbId, string tmdbId, string mediaType,
            CancellationToken cancellationToken)
        {
            var options = Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions;
            var apiKey = options?.MdbListApiKey?.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
                return new Result { Ok = false, Error = "未配置 MdbListApiKey" };

            string url;
            var type = string.Equals(mediaType, "Series", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(mediaType, "Episode", StringComparison.OrdinalIgnoreCase)
                ? "show"
                : "movie";

            if (!string.IsNullOrWhiteSpace(imdbId))
                url = $"{BaseUrl.TrimEnd('/')}/imdb/{type}/{imdbId}?apikey={apiKey}";
            else if (!string.IsNullOrWhiteSpace(tmdbId))
                url = $"{BaseUrl.TrimEnd('/')}/tmdb/{type}/{tmdbId}?apikey={apiKey}";
            else
                return new Result { Ok = false, Error = "条目没有 imdb/tmdb id" };

            try
            {
                using var http = PluginHttp.Create(TimeSpan.FromSeconds(20));
                var resp = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    Plugin.Instance?.Logger?.Info("MdbList - HTTP " + (int)resp.StatusCode + " for " + imdbId + tmdbId);
                    return new Result { Ok = false, StatusCode = (int)resp.StatusCode, Error = Trim(body) };
                }

                return Parse(body);
            }
            catch (OperationCanceledException)
            {
                return new Result { Ok = false, Error = "超时" };
            }
            catch (Exception e)
            {
                return new Result { Ok = false, Error = e.Message };
            }
        }

        /// <summary>解析 MDBList 返回（score + votes），字段缺失时返回失败。</summary>
        public static Result Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Result { Ok = false, Error = "空响应" };

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                double? score = null;
                if (root.TryGetProperty("score", out var s))
                {
                    if (s.ValueKind == JsonValueKind.Number) score = s.GetDouble();
                    else if (s.ValueKind == JsonValueKind.String &&
                             double.TryParse(s.GetString(), System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out var parsed)) score = parsed;
                }

                int? votes = null;
                if (root.TryGetProperty("votes", out var v) && v.ValueKind == JsonValueKind.Number)
                    votes = v.GetInt32();

                return score.HasValue
                    ? new Result { Ok = true, Score = score, Votes = votes }
                    : new Result { Ok = false, Error = "响应里没有 score" };
            }
            catch (Exception e)
            {
                return new Result { Ok = false, Error = "解析失败: " + e.Message };
            }
        }

        private static string Trim(string text) =>
            string.IsNullOrEmpty(text) ? string.Empty : text.Substring(0, Math.Min(160, text.Length));
    }
}
