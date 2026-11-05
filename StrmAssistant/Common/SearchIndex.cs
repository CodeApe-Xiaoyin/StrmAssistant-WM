using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 模糊 / 拼音搜索用的内存索引：每个可搜索条目一行（id + 规整后的名称和原名 + 字母掩码）。
    ///
    /// 为什么不在搜索时现查数据库：搜索补丁跑在 Emby 查询的数据库连接里，
    /// 另开连接去扫全库会在并发时把连接池占满（见 SearchEnhancementPatch 的注释）。
    /// 所以索引在后台单独建，之后靠条目新增 / 更新 / 删除事件增量维护；
    /// 搜索时只在内存里算出候选 id，再交给原查询的连接按 id 取条目（权限、媒体库范围、类型过滤都由 Emby 原样执行）。
    ///
    /// 一万个条目大约占 1~2 MB，扫一遍不到 10 毫秒；同一个查询词的结果缓存一会儿，
    /// 因为网页搜索一次会按类型分别发好几路查询。
    /// </summary>
    public static class SearchIndex
    {
        /// <summary>参与模糊/拼音匹配的条目类型（季、文件夹这类没有搜索意义的不收）。</summary>
        private static readonly string[] IndexedTypes =
        {
            "Movie", "Series", "Episode", "BoxSet", "Person", "MusicAlbum", "MusicArtist", "Audio",
            "MusicVideo", "Video", "Trailer", "Playlist", "Book", "Game", "TvChannel"
        };

        private static readonly HashSet<string> IndexedTypeSet = new HashSet<string>(IndexedTypes);

        /// <summary>一条候选：条目 id、匹配分、条目类型名。</summary>
        public struct Hit
        {
            public long Id;
            public int Score;
            public string Type;
        }

        private sealed class Entry
        {
            public long Id;
            public string Type;
            public string Name;
            public int NameMask;
            public string Original;
            public int OriginalMask;
        }

        private static readonly ConcurrentDictionary<long, Entry> Entries = new ConcurrentDictionary<long, Entry>();
        private static readonly ConcurrentDictionary<string, CacheItem> ResultCache = new ConcurrentDictionary<string, CacheItem>();

        private sealed class CacheItem
        {
            public long Version;
            public DateTime At;
            public Hit[] Hits;
        }

        private static int _state; // 0 未建 1 建立中 2 可用
        private static long _version;
        private static ILibraryManager _libraryManager;
        private static bool _subscribed;

        public static bool Ready => Volatile.Read(ref _state) == 2;
        public static int Count => Entries.Count;

        public static void Initialize(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
            if (_subscribed || libraryManager == null) return;
            _subscribed = true;
            libraryManager.ItemAdded += OnItemChanged;
            libraryManager.ItemUpdated += OnItemChanged;
            libraryManager.ItemRemoved += OnItemRemoved;
        }

        /// <summary>清空并在后台重新建立（仪表盘「重建」按钮）。</summary>
        public static void Rebuild()
        {
            if (Volatile.Read(ref _state) == 1) return; // 正在建，不重复
            Entries.Clear();
            ResultCache.Clear();
            Interlocked.Increment(ref _version);
            Volatile.Write(ref _state, 0);
            EnsureBuilding();
        }

        /// <summary>索引还没建就在后台开始建（不等待，立刻返回）。</summary>
        public static void EnsureBuilding(int delayMs = 0)
        {
            if (_libraryManager == null) return;
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;

            Task.Run(async () =>
            {
                if (delayMs > 0) await Task.Delay(delayMs).ConfigureAwait(false);
                Build();
            });
        }

        private static void Build()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var items = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    IncludeItemTypes = IndexedTypes
                });

                foreach (var item in items) Upsert(item);

                Interlocked.Increment(ref _version);
                Volatile.Write(ref _state, 2);
                Plugin.Instance?.Logger?.Info("ChineseSearch - 拼音/模糊索引已建立: " + Entries.Count + " 个条目，用时 " +
                                              sw.ElapsedMilliseconds + "ms");
            }
            catch (Exception e)
            {
                Volatile.Write(ref _state, 0); // 下次搜索再试
                Plugin.Instance?.Logger?.Info("ChineseSearch - 建立拼音/模糊索引失败: " + e.Message);
            }
        }

        private static void OnItemChanged(object sender, ItemChangeEventArgs e)
        {
            if (Volatile.Read(ref _state) == 0) return; // 没建过索引就不用维护，建的时候会整体读一遍
            try
            {
                if (Upsert(e?.Item)) Interlocked.Increment(ref _version);
            }
            catch
            {
                // 维护失败不影响 Emby 自身
            }
        }

        private static void OnItemRemoved(object sender, ItemChangeEventArgs e)
        {
            var item = e?.Item;
            if (item == null) return;
            if (Entries.TryRemove(item.InternalId, out _)) Interlocked.Increment(ref _version);
        }

        private static bool Upsert(BaseItem item)
        {
            if (item == null || item.InternalId <= 0) return false;
            if (!IndexedTypeSet.Contains(item.GetType().Name)) return false;

            var name = PinyinSearch.Normalize(item.Name);
            var original = PinyinSearch.Normalize(item.OriginalTitle);
            if (original == name) original = null;

            if (name.Length == 0 && string.IsNullOrEmpty(original))
            {
                return Entries.TryRemove(item.InternalId, out _);
            }

            var type = item.GetType().Name;
            if (Entries.TryGetValue(item.InternalId, out var old) && old.Name == name && old.Original == original &&
                old.Type == type)
                return false;

            Entries[item.InternalId] = new Entry
            {
                Id = item.InternalId,
                Type = type,
                Name = name,
                NameMask = PinyinSearch.LetterMask(name),
                Original = original,
                OriginalMask = string.IsNullOrEmpty(original) ? 0 : PinyinSearch.LetterMask(original)
            };
            return true;
        }

        /// <summary>
        /// 按模糊/拼音规则找候选条目，返回 (id, 分数)，分数高的在前，最多 limit 个。
        /// 索引没建好时返回空（并在后台开始建）。
        /// </summary>
        public static Hit[] Find(string term, bool includeOriginal = true, int limit = 300)
        {
            if (!Ready)
            {
                EnsureBuilding();
                return Array.Empty<Hit>();
            }

            var keywords = PinyinSearch.Keywords(term);
            if (!PinyinSearch.Worth(keywords)) return Array.Empty<Hit>();

            var cacheKey = (includeOriginal ? "o:" : "n:") + string.Join(" ", keywords);
            var version = Interlocked.Read(ref _version);
            if (ResultCache.TryGetValue(cacheKey, out var cached) && cached.Version == version &&
                (DateTime.UtcNow - cached.At).TotalSeconds < 60)
                return cached.Hits;

            var masks = keywords.Select(PinyinSearch.KeywordMask).ToArray();
            var hits = new List<Hit>();

            foreach (var e in Entries.Values)
            {
                var score = PinyinSearch.Score(e.Name, e.NameMask, keywords, masks);
                if (includeOriginal && !string.IsNullOrEmpty(e.Original))
                {
                    // 原名命中的排在名称命中之后
                    var o = PinyinSearch.Score(e.Original, e.OriginalMask, keywords, masks) - 5;
                    if (o > score) score = o;
                }

                if (score > 0) hits.Add(new Hit { Id = e.Id, Score = score, Type = e.Type });
            }

            var result = hits.OrderByDescending(h => h.Score).Take(limit).ToArray();

            if (ResultCache.Count > 256) ResultCache.Clear();
            ResultCache[cacheKey] = new CacheItem { Version = version, At = DateTime.UtcNow, Hits = result };
            return result;
        }
    }
}
