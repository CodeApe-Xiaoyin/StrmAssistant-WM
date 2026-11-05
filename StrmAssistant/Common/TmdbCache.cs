using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace StrmAssistant.Common
{
    /// <summary>
    /// TMDb 响应缓存（对应 PRO 的「启动时清理近期 TMDB 缓存」）。
    /// 位置：&lt;DataPath&gt;/StrmAssistant/tmdb-cache
    /// </summary>
    public static class TmdbCache
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

        /// <summary>缓存条目上限，超出后按文件修改时间淘汰最旧的（对应 PRO 的缓存维护）。</summary>
        public const int MaxEntries = 2000;

        private static int _hits;
        private static int _misses;

        public static int Hits => _hits;
        public static int Misses => _misses;

        internal static bool ShouldEvict(int entryCount) => entryCount > MaxEntries;

        /// <summary>淘汰最旧的若干条，使总数回到上限以内。</summary>
        internal static int EvictOldest(string directory, int maxEntries)
        {
            try
            {
                var files = new DirectoryInfo(directory).GetFiles("*.json");
                if (files.Length <= maxEntries) return 0;

                var excess = files.Length - maxEntries;
                Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

                var removed = 0;
                for (var i = 0; i < excess && i < files.Length; i++)
                {
                    try { files[i].Delete(); removed++; }
                    catch { }
                }

                return removed;
            }
            catch
            {
                return 0;
            }
        }

        public static object GetStats()
        {
            try
            {
                var dir = Directory;
                if (!System.IO.Directory.Exists(dir))
                {
                    return new { entries = 0, sizeBytes = 0L, hits = _hits, misses = _misses, maxEntries = MaxEntries };
                }

                var files = new DirectoryInfo(dir).GetFiles("*.json");
                var bytes = 0L;
                foreach (var f in files) bytes += f.Length;

                return new { entries = files.Length, sizeBytes = bytes, hits = _hits, misses = _misses, maxEntries = MaxEntries };
            }
            catch (System.Exception e)
            {
                return new { error = e.Message };
            }
        }

        public static string Directory
        {
            get
            {
                var dataPath = Plugin.Instance?.ApplicationPaths?.DataPath ?? Path.GetTempPath();
                return Path.Combine(dataPath, "StrmAssistant", "tmdb-cache");
            }
        }

        public static string TryGet(string url)
        {
            try
            {
                var path = GetPath(url);
                if (!File.Exists(path))
                {
                    _misses++;
                    return null;
                }

                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > Ttl)
                {
                    _misses++;
                    return null;
                }

                var text = File.ReadAllText(path);

                // 半截文件（写入中断/并发写）直接当未命中，并清掉
                try
                {
                    using var _ = System.Text.Json.JsonDocument.Parse(text);
                }
                catch
                {
                    _misses++;
                    try { File.Delete(path); } catch { }
                    return null;
                }

                _hits++;
                return text;
            }
            catch
            {
                return null;
            }
        }

        public static void Put(string url, string json)
        {
            try
            {
                var dir = Directory;
                System.IO.Directory.CreateDirectory(dir);

                // 先写临时文件再改名：避免并发读写看到半截 JSON
                var target = GetPath(url);
                var temp = target + "." + System.Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                File.WriteAllText(temp, json);

                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);

                var files = new DirectoryInfo(dir).GetFiles("*.json");
                if (ShouldEvict(files.Length)) EvictOldest(dir, MaxEntries);
            }
            catch
            {
                // 缓存失败不影响主流程
            }
        }

        public static int Clear()
        {
            try
            {
                var dir = Directory;
                if (!System.IO.Directory.Exists(dir)) return 0;

                var files = System.IO.Directory.GetFiles(dir);
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }

                return files.Length;
            }
            catch
            {
                return 0;
            }
        }

        private static string GetPath(string url)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
            var name = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            return Path.Combine(Directory, name + ".json");
        }
    }
}
