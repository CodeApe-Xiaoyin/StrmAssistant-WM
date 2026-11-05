using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 清理本地元数据缓存里已经没有对应条目的目录（条目被删除后缓存会残留）。
    /// 匹配依据：优先 TMDb/IMDb/TVDB id，其次目录名与条目标题完全一致。
    /// </summary>
    public class MetadataCacheCleanupService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        // 最近改动过的目录可能是正在写入的，10 分钟内不删
        private static readonly System.TimeSpan RecentWriteWindow = System.TimeSpan.FromMinutes(10);

        public MetadataCacheCleanupService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public int Cleanup(string rootFolder, IReadOnlyCollection<BaseItem> libraryItems)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder)) return 0;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in libraryItems)
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    // 目录名是按 Sanitize 处理过的（「:」「?」会变成「_」），新版还带年份，三种都要认
                    names.Add(item.Name);
                    names.Add(MetadataCacheService.Sanitize(item.Name));
                    names.Add(MetadataCacheService.CacheFolderName(item));
                }

                foreach (var id in item.ProviderIds.Where(i => !string.IsNullOrWhiteSpace(i.Value)))
                {
                    ids.Add(id.Value);
                }
            }

            var removed = 0;
            var skippedRecent = 0;

            foreach (var dir in Directory.GetDirectories(rootFolder))
            {
                try
                {
                    var dirName = Path.GetFileName(dir);
                    if (IsReferenced(dir, dirName, names, ids)) continue;

                    // 最近改动过的目录可能是「正在写入」的：不要删，留给下次
                    var age = System.DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir);
                    if (age < RecentWriteWindow)
                    {
                        skippedRecent++;
                        _logger.Info("MetadataCacheCleanup - skipped (recently modified) " + dirName);
                        continue;
                    }

                    if (DryRunMode.Skip("清理元数据缓存目录 " + dirName, _logger)) continue;
                    Directory.Delete(dir, true);
                    removed++;
                    _logger.Info("MetadataCacheCleanup - removed " + dirName);
                }
                catch (Exception e)
                {
                    _logger.Info("MetadataCacheCleanup - failed for " + dir + ": " + e.Message);
                }
            }

            return removed;
        }

        private static bool IsReferenced(string dir, string dirName, HashSet<string> names, HashSet<string> ids)
        {
            if (names.Contains(dirName)) return true;

            foreach (var nfoName in new[] { "tvshow.nfo", "movie.nfo" })
            {
                var nfo = Path.Combine(dir, nfoName);
                if (!File.Exists(nfo)) continue;

                try
                {
                    var root = XDocument.Load(nfo).Root;
                    if (root == null) continue;

                    foreach (var tag in new[] { "tmdbid", "imdbid", "tvdbid", "doubanid" })
                    {
                        var value = root.Element(tag)?.Value;
                        if (!string.IsNullOrWhiteSpace(value) && ids.Contains(value)) return true;
                    }
                }
                catch
                {
                    // 读不出来的缓存当作可清理
                }
            }

            return false;
        }
    }
}
