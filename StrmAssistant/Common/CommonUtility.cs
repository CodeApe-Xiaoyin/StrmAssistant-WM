using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StrmAssistant.Common
{
    public static class CommonUtility
    {
        private static readonly Regex MovieDbApiKeyRegex = new Regex("^[a-fA-F0-9]{32}$", RegexOptions.Compiled);

        public static bool IsValidHttpUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            {
                return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }


        private const string DefaultMovieDbApiHost = "https://api.themoviedb.org";
        private const string DefaultMovieDbImageHost = "https://image.tmdb.org";

        /// <summary>
        /// 移植自 PRO：启用「自定义 MovieDb」后，把默认 TMDB 域名替换成用户配置的地址，
        /// 并用用户自己的 Api Key 替换请求里的 api_key。
        /// </summary>
        /// <summary>
        /// TMDB API Key：优先用「替代 API 金钥」，留空时回落到 Emby 内置的系统默认 key。
        /// 之前直接用 AltMovieDbApiKey，留空就拼出 `api_key=` 空值 → 401，
        /// 与选项描述「留空使用系统默认」不符。
        /// </summary>
        public static string ResolveMovieDbApiKey()
        {
            var options = Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions;
            var alt = options?.AltMovieDbApiKey;
            if (!string.IsNullOrWhiteSpace(alt)) return alt.Trim();

            try
            {
                var type = Type.GetType("MovieDb.MovieDbProvider, MovieDb")
                           ?? Type.GetType("MovieDb.MovieDbProviderBase, MovieDb");
                var field = type?.GetField("ApiKey",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);
                if (field?.GetValue(null) is string key && !string.IsNullOrWhiteSpace(key)) return key;
            }
            catch
            {
                // 读不到就用下面的兜底
            }

            // Emby 内置的公开 key（与官方 provider 一致）
            return "f6bd687ffa63cd282b6ff2c6877f2669";
        }

        public static string ApplyAltMovieDbUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            var options = Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions;
            if (options == null || !options.AltMovieDbConfig) return url;

            var apiUrl = (options.AltMovieDbApiUrl ?? string.Empty).Trim().TrimEnd('/');
            var imageUrl = (options.AltMovieDbImageUrl ?? string.Empty).Trim().TrimEnd('/');

            if (!string.IsNullOrEmpty(apiUrl) && url.StartsWith(DefaultMovieDbApiHost, StringComparison.OrdinalIgnoreCase))
                url = apiUrl + url.Substring(DefaultMovieDbApiHost.Length);

            if (!string.IsNullOrEmpty(imageUrl) && url.StartsWith(DefaultMovieDbImageHost, StringComparison.OrdinalIgnoreCase))
                url = imageUrl + url.Substring(DefaultMovieDbImageHost.Length);

            var altKey = options.AltMovieDbApiKey;
            if (!string.IsNullOrWhiteSpace(altKey))
                url = Regex.Replace(url, @"api_key=[^&]*", "api_key=" + altKey);

            return url;
        }

        public static bool IsValidMovieDbApiKey(string apiKey)
        {
            return !string.IsNullOrWhiteSpace(apiKey) && MovieDbApiKeyRegex.IsMatch(apiKey);
        }

        public static bool IsValidProxyUrl(string proxyUrl)
        {
            try
            {
                var uri = new Uri(proxyUrl);
                return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                       (uri.IsDefaultPort || (uri.Port > 0 && uri.Port <= 65535)) &&
                       (string.IsNullOrEmpty(uri.UserInfo) || uri.UserInfo.Contains(":"));
            }
            catch
            {
                return false;
            }
        }

        public static bool TryParseProxyUrl(string proxyUrl, out string schema, out string host, out int port, out string username, out string password)
        {
            schema = string.Empty;
            host = string.Empty;
            port = 0;
            username = string.Empty;
            password = string.Empty;

            try
            {
                var uri = new Uri(proxyUrl);
                if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                {
                    schema = uri.Scheme;
                    host = uri.Host;
                    port = uri.IsDefaultPort ? uri.Scheme == Uri.UriSchemeHttp ? 80 : 443 : uri.Port;

                    if (!string.IsNullOrEmpty(uri.UserInfo))
                    {
                        var userInfoParts = uri.UserInfo.Split(':');
                        username = userInfoParts[0];
                        password = userInfoParts.Length > 1 ? userInfoParts[1] : string.Empty;
                    }

                    return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        public static (bool isReachable, double? tcpPing) CheckProxyReachability(string host, int port)
        {
            try
            {
                using var tcpClient = new TcpClient();
                var stopwatch = Stopwatch.StartNew();
                if (tcpClient.ConnectAsync(host, port).Wait(999))
                {
                    stopwatch.Stop();
                    return (true, stopwatch.Elapsed.TotalMilliseconds);
                }
            }
            catch
            {
                // ignored
            }

            return (false, null);
        }

        public static (bool isReachable, double? httpPing) CheckProxyReachability(string scheme, string host, int port,
            string username, string password)
        {
            double? httpPing = null;

            try
            {
                var proxyUrl = new UriBuilder(scheme, host, port).Uri;
                using var handler = new HttpClientHandler();
                handler.Proxy = new WebProxy(proxyUrl)
                {
                    Credentials = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password)
                        ? new NetworkCredential(username, password)
                        : null
                };
                handler.UseProxy = true;

                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMilliseconds(666);

                var task1 = client.GetAsync("http://www.gstatic.com/generate_204");
                var task2 = client.GetAsync("http://www.google.com/generate_204");

                var stopwatch = Stopwatch.StartNew();
                var completedTask = Task.WhenAny(task1, task2).Result;
                stopwatch.Stop();

                if (completedTask.Status == TaskStatus.RanToCompletion && completedTask.Result.IsSuccessStatusCode &&
                    completedTask.Result.StatusCode == HttpStatusCode.NoContent)
                {
                    httpPing = stopwatch.Elapsed.TotalMilliseconds;
                }
                else
                {
                    var otherTask = completedTask == task1 ? task2 : task1;
                    if (otherTask.Status == TaskStatus.RanToCompletion && otherTask.Result.IsSuccessStatusCode &&
                        otherTask.Result.StatusCode == HttpStatusCode.NoContent)
                    {
                        httpPing = stopwatch.Elapsed.TotalMilliseconds;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return (httpPing.HasValue, httpPing);
        }

        public static string GenerateFixedCode(string input, string prefix, int length)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return prefix + BitConverter.ToString(hash).Replace("-", "").Substring(0, length).ToLower();
        }

        public static long Find(long x, Dictionary<long, long> parent)
        {
            if (parent[x] == x) return x;
            return parent[x] = Find(parent[x], parent);
        }

        public static void Union(long x, long y, Dictionary<long, long> parent)
        {
            var root1 = Find(x, parent);
            var root2 = Find(y, parent);
            if (root1 != root2) parent[root1] = root2;
        }

        public static bool IsDirectoryEmpty(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return false;

            var entries = Directory.EnumerateFileSystemEntries(directoryPath).Take(1);

            return !entries.Any();
        }

        public static double LevenshteinDistance(string str1, string str2)
        {
            int n = str1.Length;
            int m = str2.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) ;
            for (int j = 0; j <= m; d[0, j] = j++) ;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (str1[i - 1] == str2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            int levenshteinDistance = d[n, m];
            double similarity = 1.0 - (levenshteinDistance / (double)Math.Max(str1.Length, str2.Length));

            return similarity;
        }
    }
}
