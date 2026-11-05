using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Entities;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO「NFO 元数据导入增强」：读取 NFO 里演员的图片地址并落地为人物头像。
    /// </summary>
    public class NfoService
    {
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        public NfoService(IProviderManager providerManager, IFileSystem fileSystem, ILogger logger)
        {
            _providerManager = providerManager;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public static string ResolveNfoPath(BaseItem item)
        {
            if (string.IsNullOrEmpty(item.Path)) return null;
            if (Directory.Exists(item.Path))
            {
                var tvshow = Path.Combine(item.Path, "tvshow.nfo");
                if (File.Exists(tvshow)) return tvshow;

                var movieNfo = Path.Combine(item.Path, Path.GetFileName(item.Path) + ".nfo");
                return File.Exists(movieNfo) ? movieNfo : null;
            }

            var candidate = Path.ChangeExtension(item.Path, ".nfo");
            return File.Exists(candidate) ? candidate : null;
        }

        public static List<(string Name, string ThumbUrl)> ParseActors(string nfoPath)
        {
            var result = new List<(string, string)>();
            if (string.IsNullOrEmpty(nfoPath) || !File.Exists(nfoPath)) return result;

            try
            {
                var doc = Common.TextFile.LoadXml(nfoPath);
                foreach (var actor in doc.Descendants("actor"))
                {
                    var name = actor.Element("name")?.Value;
                    var thumb = actor.Element("thumb")?.Value;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(thumb))
                        result.Add((name.Trim(), thumb.Trim()));
                }
            }
            catch
            {
                // NFO 格式异常时忽略
            }

            return result;
        }

        /// <summary>把演员头像写入 Emby 的人物元数据目录。</summary>
        public async Task<int> ApplyActorImagesAsync(string nfoPath, string peopleRoot, CancellationToken token)
        {
            var actors = ParseActors(nfoPath);
            var applied = 0;

            foreach (var (name, url) in actors)
            {
                if (token.IsCancellationRequested) break;

                var bytes = await DownloadAsync(url, token).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0) continue;

                // NFO 是外部文件：演员名直接拼路径会有两个风险
                // 1) 名字里带 / 或 .. 会写到人物目录之外
                // 2) 中文长名会超过文件系统 255 字节上限
                var safeName = MetadataCacheService.Sanitize(name);
                if (string.IsNullOrWhiteSpace(safeName) || safeName == "." || safeName == "..") continue;

                var dir = Path.Combine(peopleRoot, safeName);
                var rootFull = Path.GetFullPath(peopleRoot);
                var dirFull = Path.GetFullPath(dir);
                if (!dirFull.StartsWith(rootFull, StringComparison.Ordinal)) continue;

                if (DryRunMode.Skip("写入演员头像 " + name, _logger)) continue;

                Directory.CreateDirectory(dir);
                await _fileSystem.WriteAllBytesAsync(Path.Combine(dir, "folder.jpg"), bytes, token)
                    .ConfigureAwait(false);
                applied++;
            }

            return applied;
        }

        /// <summary>单张演员头像的大小上限，防止 NFO 指向超大文件把内存打满。</summary>
        private const long MaxImageBytes = 8L * 1024 * 1024;

        private async Task<byte[]> DownloadAsync(string url, CancellationToken token)
        {
            var httpClient = Plugin.HttpClient;
            if (httpClient == null) return null;

            // 只允许 http/https：NFO 可以指向 file:// 或其它协议
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                _logger.Info("NfoService - unsupported image url skipped: " + url);
                return null;
            }

            var options = new HttpRequestOptions
            {
                Url = url,
                CancellationToken = token,
                BufferContent = true,
                UserAgent = Plugin.Instance.UserAgent
            };

            using var response = await httpClient.SendAsync(options, "GET").ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.OK) return null;

            await using var stream = response.Content;

            if (stream.Length > MaxImageBytes)
            {
                _logger.Info($"NfoService - image too large skipped ({stream.Length} bytes): " + url);
                return null;
            }

            using var buffer = new System.IO.MemoryStream();
            var chunk = new byte[81920];
            long total = 0;

            while (true)
            {
                var read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false);
                if (read <= 0) break;

                total += read;
                if (total > MaxImageBytes)
                {
                    _logger.Info("NfoService - image exceeded limit while downloading, aborted: " + url);
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
    }
}
