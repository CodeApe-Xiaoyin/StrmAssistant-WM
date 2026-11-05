using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class CacheVerifyService : IService
    {
        public object Get(CacheVerifyApi request)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            var root = options?.MetadataCacheRootFolder;
            var enabled = options?.MetadataCacheMode == true;

            if (!enabled || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return new { enabled, root, message = "缓存未启用或目录不存在" };
            }

            var libraryManager = Plugin.LibraryManager;
            if (libraryManager == null) return new { enabled, root, message = "媒体库不可用" };

            var items = libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { typeof(Series).Name, typeof(Movie).Name }
            });

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.Name)) names.Add(item.Name);
                foreach (var id in item.ProviderIds.Where(i => !string.IsNullOrWhiteSpace(i.Value))) ids.Add(id.Value);
            }

            var cachedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orphans = new List<string>();

            foreach (var dir in Directory.GetDirectories(root))
            {
                var dirName = Path.GetFileName(dir);
                cachedNames.Add(dirName);

                if (names.Contains(dirName)) continue;

                var referenced = false;
                foreach (var nfoName in new[] { "tvshow.nfo", "movie.nfo" })
                {
                    var nfo = Path.Combine(dir, nfoName);
                    if (!File.Exists(nfo)) continue;

                    try
                    {
                        var nfoRoot = XDocument.Load(nfo).Root;
                        if (nfoRoot == null) continue;

                        foreach (var tag in new[] { "tmdbid", "imdbid", "tvdbid" })
                        {
                            var value = nfoRoot.Element(tag)?.Value;
                            if (!string.IsNullOrWhiteSpace(value) && ids.Contains(value)) referenced = true;
                        }
                    }
                    catch { }
                }

                if (!referenced) orphans.Add(dirName);
            }

            var missing = items.Select(i => i.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n) && !cachedNames.Contains(n))
                .Distinct()
                .ToList();

            return new
            {
                enabled,
                root,
                cacheFolders = cachedNames.Count,
                orphans,
                missing
            };
        }
    }
}
