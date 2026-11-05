using System;
using System.IO;
using System.Text;
using StrmAssistant.Common;
using System.Linq;

namespace LogicTests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static bool AllEnabled(object options, string listProp)
        {
            var list = (System.Collections.IEnumerable)options.GetType().GetProperty(listProp).GetValue(options);
            var any = false;
            foreach (var o in list)
            {
                any = true;
                if (!(bool)o.GetType().GetProperty("IsEnabled").GetValue(o)) return false;
            }
            return any;
        }

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok)
            {
                _passed++;
                Console.WriteLine("PASS " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("FAIL " + name + (detail == null ? "" : " -> " + detail));
            }
        }

        private static int Main()
        {
            // 拼音排序
            Check("pinyin 沙丘 -> SQ", SortNameHelper.BuildSortName("沙丘") == "SQ",
                SortNameHelper.BuildSortName("沙丘"));
            Check("pinyin 含英文", SortNameHelper.BuildSortName("沙丘 Dune").StartsWith("SQ"),
                SortNameHelper.BuildSortName("沙丘 Dune"));
            Check("pinyin 纯英文保持原样", SortNameHelper.BuildSortName("Dune") == "Dune",
                SortNameHelper.BuildSortName("Dune"));

            Check("containsChinese 中文", SortNameHelper.ContainsChinese("沙丘"));
            Check("containsChinese 英文", !SortNameHelper.ContainsChinese("Dune"));

            // 简繁转换
            Check("繁体转换", ChineseTextHelper.ToTraditional("简体中文测试") == "簡體中文測試",
                ChineseTextHelper.ToTraditional("简体中文测试"));
            Check("简体转换", ChineseTextHelper.ToSimplified("簡體中文測試") == "简体中文测试",
                ChineseTextHelper.ToSimplified("簡體中文測試"));
            Check("空串安全", ChineseTextHelper.ToTraditional("") == "");

            // 内存看门狗决策
            MemoryWatchdog.Reset();
            var now = DateTimeOffset.UtcNow;
            Check("watchdog disabled", MemoryWatchdog.Evaluate(900, null, true, now) == "disabled");
            Check("watchdog within-limit", MemoryWatchdog.Evaluate(300, 500, true, now) == "within-limit");
            Check("watchdog busy", MemoryWatchdog.Evaluate(900, 500, false, now) == "busy");
            MemoryWatchdog.Reset();
            Check("watchdog waiting 0m", MemoryWatchdog.Evaluate(900, 500, true, now) == "exceeded-waiting(0min)");
            Check("watchdog waiting 10m", MemoryWatchdog.Evaluate(900, 500, true, now.AddMinutes(10)) == "exceeded-waiting(10min)");
            Check("watchdog restart 16m", MemoryWatchdog.Evaluate(900, 500, true, now.AddMinutes(16)) == "exceeded-restart");
            Check("watchdog reset on within-limit",
                MemoryWatchdog.Evaluate(100, 500, true, now.AddMinutes(20)) == "within-limit" &&
                MemoryWatchdog.Evaluate(900, 500, true, now.AddMinutes(21)) == "exceeded-waiting(0min)");

            // 本地剧集组 JSON 解析
            var tmp = Path.Combine(Path.GetTempPath(), "strm-episodegroup-test.json");
            File.WriteAllText(tmp, @"{
              ""id"": ""g"", ""name"": ""g"",
              ""groups"": [
                { ""id"": ""a"", ""name"": ""A"", ""order"": 1, ""episodes"": [
                    { ""id"": ""e1"", ""name"": ""E1"", ""order"": 1, ""season_number"": 2, ""episode_number"": 1 } ] },
                { ""id"": ""b"", ""name"": ""B"", ""order"": 2, ""episodes"": [
                    { ""id"": ""e2"", ""name"": ""E2"", ""order"": 1, ""season_number"": 3, ""episode_number"": 1 } ] }
              ] }");

            var mapping = StrmAssistant.Web.Service.ApplyEpisodeGroupService.LoadLocalMapping(tmp);
            Check("local mapping 解析条数", mapping != null && mapping.Count == 2,
                mapping == null ? "null" : mapping.Count.ToString());
            if (mapping != null && mapping.Count == 2)
            {
                Check("local mapping 季集号", mapping[0].SeasonNumber == 2 && mapping[0].EpisodeNumber == 1 &&
                                             mapping[1].SeasonNumber == 3 && mapping[1].EpisodeNumber == 1);
                Check("local mapping 顺序号", mapping[0].DisplayOrder == 1 && mapping[1].DisplayOrder == 2);
            }
            Check("local mapping 文件不存在返回 null",
                StrmAssistant.Web.Service.ApplyEpisodeGroupService.LoadLocalMapping("/nonexistent/x.json") == null);

            File.Delete(tmp);

            // 缓存文件名清洗
            Check("sanitize 正常名", MetadataCacheService.Sanitize("沙丘 2026") == "沙丘 2026");
            Check("sanitize 非法字符", !MetadataCacheService.Sanitize("a/b\\c:d*e?f").Contains("/") &&
                                      !MetadataCacheService.Sanitize("a/b\\c:d*e?f").Contains(":"),
                MetadataCacheService.Sanitize("a/b\\c:d*e?f"));
            Check("sanitize 空值", MetadataCacheService.Sanitize("   ") == "unknown");
            Check("sanitize 还原侧一致",
                MetadataCacheService.Sanitize("a/b") == MetadataCacheRestoreService.Sanitize("a/b"));

            // NFO 生成
            var series = new MediaBrowser.Controller.Entities.TV.Series
            {
                Name = "沙丘 & <测试>",
                Overview = "简介 <plot>",
                ProductionYear = 2026,
                CommunityRating = 7.7f,
                PremiereDate = new DateTime(2026, 6, 1)
            };
            series.ProviderIds["Tmdb"] = "1399";
            series.ProviderIds["Douban"] = "1292052";
            series.ProviderIds["Unknown"] = "x";

            var nfo = MetadataCacheService.BuildNfo(series);
            Check("nfo 含转义标题", nfo.Contains("沙丘 &amp; &lt;测试&gt;"), nfo.Split('\n').FirstOrDefault(l => l.Contains("title")));
            Check("nfo 含 plot 转义", nfo.Contains("简介 &lt;plot&gt;"));
            Check("nfo 含年份", nfo.Contains("<year>2026</year>"));
            Check("nfo 含评分", nfo.Contains("<rating>7.7</rating>"));
            Check("nfo 含首播日", nfo.Contains("<premiered>2026-06-01</premiered>"));
            Check("nfo 含 tmdbid", nfo.Contains("<tmdbid>1399</tmdbid>"));
            Check("nfo 含 doubanid", nfo.Contains("<doubanid>1292052</doubanid>"));
            Check("nfo 过滤未知 id", !nfo.Contains("Unknown"));
            Check("nfo 根节点", nfo.Contains("<tvshow>") && nfo.Contains("</tvshow>"));

            // 分集 nfo
            var tv = new MediaBrowser.Controller.Entities.TV.Series { Name = "沙丘" };
            var ep = new MediaBrowser.Controller.Entities.TV.Episode
            {
                Name = "第1集",
                Overview = "分集简介",
                ParentIndexNumber = 1,
                IndexNumber = 3
            };
            var epNfo = MetadataCacheService.BuildEpisodeNfo(ep, tv);
            Check("episode nfo 季集号", epNfo.Contains("<season>1</season>") && epNfo.Contains("<episode>3</episode>"));
            Check("episode nfo 剧名", epNfo.Contains("<showtitle>沙丘</showtitle>"));
            Check("episode nfo 根节点", epNfo.Contains("<episodedetails>") && epNfo.Contains("</episodedetails>"));

            // 缓存新鲜度判定
            var freshDir = Path.Combine(Path.GetTempPath(), "strm-freshness-test");
            Directory.CreateDirectory(freshDir);
            var freshFile = Path.Combine(freshDir, "tvshow.nfo");

            // DateLastSaved 默认为 DateTime.MinValue，需要显式设置为「刚修改过」才有可比性
            var itemJustSaved = new MediaBrowser.Controller.Entities.TV.Series
            {
                Name = "justSaved",
                DateLastSaved = DateTime.UtcNow
            };
            var itemOld = itemJustSaved;
            var itemNew = itemJustSaved;

            Check("freshness 文件不存在 -> 不新鲜", !MetadataCacheService.IsCacheFresh(freshFile, itemOld));

            File.WriteAllText(freshFile, "x");
            Check("freshness 缓存比条目新 -> 新鲜", MetadataCacheService.IsCacheFresh(freshFile, itemNew));

            File.SetLastWriteTimeUtc(freshFile, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Check("freshness 缓存比条目旧 -> 不新鲜", !MetadataCacheService.IsCacheFresh(freshFile, itemOld));

            File.SetLastWriteTimeUtc(freshFile, DateTime.UtcNow.AddMinutes(5));
            Check("freshness 未来时间 -> 新鲜", MetadataCacheService.IsCacheFresh(freshFile, itemNew));

            Directory.Delete(freshDir, true);

            // 重试策略
            Check("retry 退避 300ms", RetryPolicy.DelayAfterAttempt(1) == TimeSpan.FromMilliseconds(300),
                RetryPolicy.DelayAfterAttempt(1).TotalMilliseconds.ToString());
            Check("retry 退避 900ms", RetryPolicy.DelayAfterAttempt(2) == TimeSpan.FromMilliseconds(900),
                RetryPolicy.DelayAfterAttempt(2).TotalMilliseconds.ToString());
            Check("retry 退避 2700ms", RetryPolicy.DelayAfterAttempt(3) == TimeSpan.FromMilliseconds(2700),
                RetryPolicy.DelayAfterAttempt(3).TotalMilliseconds.ToString());
            Check("retry 次数上限 3", RetryPolicy.MaxAttempts == 3);
            Check("retry 500 应重试", RetryPolicy.ShouldRetryStatus(500));
            Check("retry 503 应重试", RetryPolicy.ShouldRetryStatus(503));
            Check("retry 429 应重试", RetryPolicy.ShouldRetryStatus(429));
            Check("retry 404 不重试", !RetryPolicy.ShouldRetryStatus(404));
            Check("retry 400 不重试", !RetryPolicy.ShouldRetryStatus(400));
            Check("retry 200 不重试", !RetryPolicy.ShouldRetryStatus(200));
            Check("retry 取消不重试",
                !RetryPolicy.ShouldRetryException(new OperationCanceledException()));
            Check("retry 网络异常重试",
                RetryPolicy.ShouldRetryException(new System.Net.Http.HttpRequestException("x")));

            // 缓存淘汰策略
            Check("evict 未超上限不淘汰", !TmdbCache.ShouldEvict(TmdbCache.MaxEntries));
            Check("evict 超上限需淘汰", TmdbCache.ShouldEvict(TmdbCache.MaxEntries + 1));

            var evictDir = Path.Combine(Path.GetTempPath(), "strm-evict-test");
            if (Directory.Exists(evictDir)) Directory.Delete(evictDir, true);
            Directory.CreateDirectory(evictDir);
            for (var i = 0; i < 5; i++)
            {
                var f = Path.Combine(evictDir, $"entry{i}.json");
                File.WriteAllText(f, "{}");
                File.SetLastWriteTimeUtc(f, new DateTime(2020, 1, 1 + i, 0, 0, 0, DateTimeKind.Utc));
            }
            var removedCount = TmdbCache.EvictOldest(evictDir, 3);
            Check("evict 删除多余条目数", removedCount == 2, removedCount.ToString());
            Check("evict 剩余数量正确", Directory.GetFiles(evictDir).Length == 3,
                Directory.GetFiles(evictDir).Length.ToString());
            Check("evict 保留最新三条",
                File.Exists(Path.Combine(evictDir, "entry2.json")) &&
                File.Exists(Path.Combine(evictDir, "entry3.json")) &&
                File.Exists(Path.Combine(evictDir, "entry4.json")) &&
                !File.Exists(Path.Combine(evictDir, "entry0.json")));
            Directory.Delete(evictDir, true);

            // 标记前集已看：阈值与顺序判定
            Check("progress 未到阈值", !EpisodeProgressPolicy.ReachedThreshold(50, 100, 90));
            Check("progress 正好阈值", EpisodeProgressPolicy.ReachedThreshold(90, 100, 90));
            Check("progress 超过阈值", EpisodeProgressPolicy.ReachedThreshold(95, 100, 90));
            Check("progress 无运行时长不触发", !EpisodeProgressPolicy.ReachedThreshold(95, null, 90));
            Check("progress 时长为 0 不触发", !EpisodeProgressPolicy.ReachedThreshold(95, 0, 90));
            Check("progress 位置为 0 不触发", !EpisodeProgressPolicy.ReachedThreshold(0, 100, 90));
            Check("order 同季集号小在前", EpisodeProgressPolicy.IsBefore(1, 2, 1, 5));
            Check("order 同季集号大不在前", !EpisodeProgressPolicy.IsBefore(1, 5, 1, 2));
            Check("order 上季在前", EpisodeProgressPolicy.IsBefore(1, 9, 2, 1));
            Check("order 下季不在前", !EpisodeProgressPolicy.IsBefore(2, 1, 1, 9));
            Check("order 季号未知的不自动标记", !EpisodeProgressPolicy.IsBefore(null, null, 1, 1));

            // TMDb 缓存：写入 / 命中 / TTL 过期
            var cacheDir = StrmAssistant.Common.TmdbCache.Directory;
            if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
            Directory.CreateDirectory(cacheDir);

            StrmAssistant.Common.TmdbCache.Put("https://example.test/a", "{\"v\":1}");
            var got = StrmAssistant.Common.TmdbCache.TryGet("https://example.test/a");
            Check("cache 写入后可读出", got == "{\"v\":1}", got ?? "null");

            var miss = StrmAssistant.Common.TmdbCache.TryGet("https://example.test/missing");
            Check("cache 未命中返回 null", miss == null);

            var stats = StrmAssistant.Common.TmdbCache.GetStats();
            var statsType = stats.GetType();
            var hits = System.Convert.ToInt32(statsType.GetProperty("hits").GetValue(stats));
            var misses = System.Convert.ToInt32(statsType.GetProperty("misses").GetValue(stats));
            Check("cache 命中计数", hits >= 1, "hits=" + hits);
            Check("cache 未命中计数", misses >= 1, "misses=" + misses);

            // 把文件改成 30 天前，应当视为过期
            foreach (var f in Directory.GetFiles(cacheDir))
                File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-30));

            var expired = StrmAssistant.Common.TmdbCache.TryGet("https://example.test/a");
            Check("cache 超 TTL 视为未命中", expired == null, expired ?? "null");

            Directory.Delete(cacheDir, true);

            // 字幕优化
            var subDir = Path.Combine(Path.GetTempPath(), "strm-subtitle-test");
            if (Directory.Exists(subDir)) Directory.Delete(subDir, true);
            Directory.CreateDirectory(subDir);

            // UTF-16LE + BOM + 繁体
            var subUtf16 = Path.Combine(subDir, "u16.srt");
            var trad = "1\r\n00:00:01,000 --> 00:00:03,000\r\n這是繁體字幕\r\n";
            File.WriteAllBytes(subUtf16, new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(trad)).ToArray());
            var changed16 = StrmAssistant.Common.SubtitleOptimizer.Optimize(subUtf16, true, out var msg16);
            var text16 = File.ReadAllText(subUtf16, Encoding.UTF8);
            Check("subtitle UTF-16 转 UTF-8", changed16, msg16);
            Check("subtitle 繁体转简体", text16.Contains("这是繁体字幕"), text16.Replace("\r\n", " ").Trim());
            Check("subtitle 转换后无 BOM", File.ReadAllBytes(subUtf16)[0] != 0xEF);

            // UTF-8 BOM
            var subBom = Path.Combine(subDir, "bom.srt");
            File.WriteAllBytes(subBom, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("简体字幕测试\n")).ToArray());
            var changedBom = StrmAssistant.Common.SubtitleOptimizer.Optimize(subBom, true, out var msgBom);
            Check("subtitle UTF-8 BOM 被去掉", changedBom && File.ReadAllBytes(subBom)[0] != 0xEF, msgBom);

            // 已是最优状态：再跑一次不应改动
            var changedAgain = StrmAssistant.Common.SubtitleOptimizer.Optimize(subBom, true, out var msgAgain);
            Check("subtitle 幂等（第二次不改动）", !changedAgain, msgAgain);

            // 不支持的扩展名
            var badExt = Path.Combine(subDir, "note.txt");
            File.WriteAllText(badExt, "hello");
            Check("subtitle 不支持的格式返回 false",
                !StrmAssistant.Common.SubtitleOptimizer.Optimize(badExt, true, out var msgBad), msgBad);

            // 文件不存在
            Check("subtitle 文件不存在返回 false",
                !StrmAssistant.Common.SubtitleOptimizer.Optimize(Path.Combine(subDir, "none.srt"), true, out var msgNone), msgNone);

            // GB18030（中文字幕常见编码）——.NET Core 需要注册 CodePages 提供程序才能解码
            var subGb = Path.Combine(subDir, "gb.srt");
            File.WriteAllBytes(subGb, Convert.FromBase64String("MQ0KMDA6MDA6MDEsMDAwIC0tPiAwMDowMDowMywwMDANCrzyzOXW0M7EsuLK1A0K"));
            StrmAssistant.Common.SubtitleOptimizer.Optimize(subGb, true, out var msgGb);
            var textGb = File.ReadAllText(subGb, Encoding.UTF8);
            Check("subtitle GB18030 正确解码", textGb.Contains("简体中文测试"),
                textGb.Replace("\r\n", " ").Trim() + " | " + msgGb);

            Directory.Delete(subDir, true);

            // 外部文本读取：编码兜底
            var txtDir = Path.Combine(Path.GetTempPath(), "strm-textfile-test");
            if (Directory.Exists(txtDir)) Directory.Delete(txtDir, true);
            Directory.CreateDirectory(txtDir);

            var gbkPath = Path.Combine(txtDir, "gbk.nfo");
            File.WriteAllBytes(gbkPath, Convert.FromBase64String("PG12PjxkZXNjcmlwdGlvbj7O99POvMc8L2Rlc2NyaXB0aW9uPjwvbXY+"));
            var gbkText = StrmAssistant.Common.TextFile.ReadAllText(gbkPath);
            Check("TextFile GBK/GB18030 解码", gbkText.Contains("西游记"), gbkText);

            var bomPath = Path.Combine(txtDir, "bom.json");
            File.WriteAllBytes(bomPath, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"a\":1}")).ToArray());
            var bomText = StrmAssistant.Common.TextFile.ReadAllText(bomPath);
            Check("TextFile 去掉 UTF-8 BOM", bomText.StartsWith("{"), bomText);

            var utf16Path = Path.Combine(txtDir, "u16.json");
            File.WriteAllBytes(utf16Path, new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("{\"b\":2}")).ToArray());
            Check("TextFile UTF-16 解码", StrmAssistant.Common.TextFile.ReadAllText(utf16Path) == "{\"b\":2}");

            var xmlPath = Path.Combine(txtDir, "gbk.nfo");
            var doc = StrmAssistant.Common.TextFile.LoadXml(xmlPath);
            Check("TextFile 解析 GBK 编码的 XML", doc?.Root?.Element("description")?.Value == "西游记",
                doc?.Root?.Element("description")?.Value ?? "(null)");

            Check("TextFile 文件不存在返回 null", StrmAssistant.Common.TextFile.ReadAllText(Path.Combine(txtDir, "none.txt")) == null);

            Directory.Delete(txtDir, true);

            // 超长中文名：Linux 单层路径上限 255 字节
            var longName = new string('长', 120);   // UTF-8 下 360 字节
            var sanitizedLong = StrmAssistant.Common.MetadataCacheService.Sanitize(longName);
            var bytesLong = Encoding.UTF8.GetByteCount(sanitizedLong);
            Check("Sanitize 超长中文名不超过字节上限", bytesLong <= StrmAssistant.Common.MetadataCacheService.MaxNameBytes,
                bytesLong + " bytes");

            var otherLong = new string('长', 119) + "短";
            var sanitizedOther = StrmAssistant.Common.MetadataCacheService.Sanitize(otherLong);
            Check("Sanitize 截断后仍可区分不同名字", sanitizedLong != sanitizedOther,
                sanitizedLong.Substring(System.Math.Max(0, sanitizedLong.Length - 10)) + " vs " +
                sanitizedOther.Substring(System.Math.Max(0, sanitizedOther.Length - 10)));

            Check("Sanitize 短名字不被改动", StrmAssistant.Common.MetadataCacheService.Sanitize("沙丘") == "沙丘");
            Check("Sanitize 非法字符替换", StrmAssistant.Common.MetadataCacheService.Sanitize("a/b:c") == "a_b_c");
            Check("Sanitize 路径穿越被消解",
                !StrmAssistant.Common.MetadataCacheService.Sanitize("../../evil").Contains("/"),
                StrmAssistant.Common.MetadataCacheService.Sanitize("../../evil"));
            Check("Sanitize 反斜杠穿越被消解",
                !StrmAssistant.Common.MetadataCacheService.Sanitize("..\\..\\evil").Contains("\\"),
                StrmAssistant.Common.MetadataCacheService.Sanitize("..\\..\\evil"));

            // 排序键：混合脚本 / 数字 / 标点
            Check("SortName 纯中文", StrmAssistant.Common.SortNameHelper.BuildSortName("三国演义") == "SGYY");
            Check("SortName 数字开头", StrmAssistant.Common.SortNameHelper.BuildSortName("3体") == "3T");
            Check("SortName 中英混排保留英文",
                StrmAssistant.Common.SortNameHelper.BuildSortName("沙丘 Dune") == "SQ Dune",
                StrmAssistant.Common.SortNameHelper.BuildSortName("沙丘 Dune"));
            Check("SortName 全角冒号被去掉",
                StrmAssistant.Common.SortNameHelper.BuildSortName("007：幽灵党") == "007YLD",
                StrmAssistant.Common.SortNameHelper.BuildSortName("007：幽灵党"));
            Check("SortName 全角括号被去掉",
                StrmAssistant.Common.SortNameHelper.BuildSortName("（测试）剧名") == "CSJM",
                StrmAssistant.Common.SortNameHelper.BuildSortName("（测试）剧名"));
            Check("SortName 斜杠被去掉但保留空格",
                StrmAssistant.Common.SortNameHelper.BuildSortName("A/B 测试") == "AB CS",
                StrmAssistant.Common.SortNameHelper.BuildSortName("A/B 测试"));
            Check("SortName 纯标点回退到原始拼音",
                StrmAssistant.Common.SortNameHelper.BuildSortName("：：：") != "",
                StrmAssistant.Common.SortNameHelper.BuildSortName("：：："));
            Check("SortName 空字符串原样返回",
                StrmAssistant.Common.SortNameHelper.BuildSortName("") == "");

            // 搜索名归一化
            Check("SearchName 去掉年份括号",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("沙丘 (2021)") == "沙丘",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("沙丘 (2021)"));
            Check("SearchName 全角括号也去掉",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("沙丘（2021）") == "沙丘",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("沙丘（2021）"));
            Check("SearchName 全角字母转半角",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("ＳＱＵＩＤ　ＧＡＭＥ") == "SQUID GAME",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("ＳＱＵＩＤ　ＧＡＭＥ"));
            Check("SearchName 压缩多余空格",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("沙丘   2021") == "沙丘 2021",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("沙丘   2021"));
            Check("SearchName 括号内容全去掉时回退",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("(2021)") == "(2021)" ||
                StrmAssistant.Common.SearchNameNormalizer.Normalize("(2021)") == "2021",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("(2021)"));
            Check("SearchName 空值返回空串",
                StrmAssistant.Common.SearchNameNormalizer.Normalize("") == "");

            // 剧集组映射的季集号校验（外部 JSON 可能非法）
            bool ValidEpg(int season, int episode) =>
                StrmAssistant.Web.Service.ApplyEpisodeGroupService.IsValidNumber(
                    new StrmAssistant.Dto.MovieDbGroupEpisode { SeasonNumber = season, EpisodeNumber = episode });

            Check("EpisodeGroup 季集号校验 正常", ValidEpg(1, 1) && ValidEpg(0, 12));
            Check("EpisodeGroup 季集号校验 拒绝负数季", !ValidEpg(-1, 1));
            Check("EpisodeGroup 季集号校验 拒绝第 0 集", !ValidEpg(1, 0));
            Check("EpisodeGroup 季集号校验 拒绝超大值", !ValidEpg(1, 10001) && !ValidEpg(10001, 1));
            Check("EpisodeGroup 季集号校验 拒绝 null",
                !StrmAssistant.Web.Service.ApplyEpisodeGroupService.IsValidNumber(null));

            // 片头标记校验（外部数据不可信）
            StrmAssistant.Dto.IntroMarker M(double a, double b, double c = 0) =>
                new StrmAssistant.Dto.IntroMarker { IntroStart = a, IntroEnd = b, CreditsStart = c, Provider = "t" };

            var neg = StrmAssistant.Common.IntroMarkerValidator.Normalize(M(-5, 30), 600);
            Check("IntroMarker 负数开始被压到 0", neg != null && neg.IntroStart == 0, neg?.IntroStart.ToString() ?? "null");
            Check("IntroMarker 首尾颠倒被拒绝", StrmAssistant.Common.IntroMarkerValidator.Normalize(M(30, 10), 600) == null);
            Check("IntroMarker 超出片长被裁剪",
                StrmAssistant.Common.IntroMarkerValidator.Normalize(M(10, 9999), 600)?.IntroEnd == 600);
            Check("IntroMarker 开始超过片长被拒绝",
                StrmAssistant.Common.IntroMarkerValidator.Normalize(M(700, 800), 600) == null);
            Check("IntroMarker 过短的片头被拒绝",
                StrmAssistant.Common.IntroMarkerValidator.Normalize(M(10, 10.3), 600) == null);
            Check("IntroMarker 片尾早于片头结束被丢弃",
                StrmAssistant.Common.IntroMarkerValidator.Normalize(M(10, 60, 40), 600)?.CreditsStart == 0);
            Check("IntroMarker NaN 被拒绝",
                StrmAssistant.Common.IntroMarkerValidator.Normalize(M(double.NaN, 30), 600) == null);
            Check("IntroMarker 正常值原样保留",
                StrmAssistant.Common.IntroMarkerValidator.Normalize(M(5, 95, 1300), 1500)?.IntroEnd == 95);
            Check("IntroMarker null 输入返回 null",
                StrmAssistant.Common.IntroMarkerValidator.Normalize(null, 600) == null);

            // 评分范围守卫
            Check("RatingGuard 正常值保留", StrmAssistant.Common.RatingGuard.Normalize(8.3) == 8.3f);
            Check("RatingGuard 0 分被拒绝", StrmAssistant.Common.RatingGuard.Normalize(0) == null);
            Check("RatingGuard 负数被拒绝", StrmAssistant.Common.RatingGuard.Normalize(-1) == null);
            Check("RatingGuard 百分制被拒绝", StrmAssistant.Common.RatingGuard.Normalize(85) == null);
            Check("RatingGuard 恰好 10 分保留", StrmAssistant.Common.RatingGuard.Normalize(10) == 10f);
            Check("RatingGuard NaN 被拒绝", StrmAssistant.Common.RatingGuard.Normalize(double.NaN) == null);
            Check("RatingGuard null 返回 null", StrmAssistant.Common.RatingGuard.Normalize(null) == null);

            // 配置导入的路径/类型防护
            var opts = new StrmAssistant.Options.PluginOptions();
            Check("ConfigImport 正常键可写入",
                StrmAssistant.Web.Service.ConfigTransferService.Apply(opts,
                    "MetadataEnhanceOptions.PinyinSortName", "true") &&
                opts.MetadataEnhanceOptions.PinyinSortName);
            Check("ConfigImport 拒绝单层键",
                !StrmAssistant.Web.Service.ConfigTransferService.Apply(opts, "DebugMode", "true"));
            Check("ConfigImport 拒绝不存在的键",
                !StrmAssistant.Web.Service.ConfigTransferService.Apply(opts, "MetadataEnhanceOptions.NotAField", "1"));
            Check("ConfigImport 拒绝非法布尔值",
                !StrmAssistant.Web.Service.ConfigTransferService.Apply(opts,
                    "MetadataEnhanceOptions.PinyinSortName", "notabool"));
            Check("ConfigImport 拒绝非法枚举值",
                !StrmAssistant.Web.Service.ConfigTransferService.Apply(opts,
                    "ExperienceEnhanceOptions.UIFunctionOptions.ItemOrder", "NoSuchValue"));
            Check("ConfigImport 拒绝给值类型写 null",
                !StrmAssistant.Web.Service.ConfigTransferService.Apply(opts,
                    "MetadataEnhanceOptions.PinyinSortName", null));
            Check("ConfigImport 拒绝框架类型下钻",
                !StrmAssistant.Web.Service.ConfigTransferService.Apply(opts,
                    "GeneralOptions.Culture.InvariantCulture", "x"));
            Check("ConfigImport 空路径被拒绝",
                !StrmAssistant.Web.Service.ConfigTransferService.Apply(opts, "", "x"));

            // 功能开关指纹（用于判断是否需要全量重跑）
            var f1 = new StrmAssistant.Options.MetadataEnhanceOptions();
            var f2 = new StrmAssistant.Options.MetadataEnhanceOptions();
            var fp1 = StrmAssistant.ScheduledTask.MetadataEnhanceTask.ComputeOptionsFingerprint(f1);
            var fp2 = StrmAssistant.ScheduledTask.MetadataEnhanceTask.ComputeOptionsFingerprint(f2);
            Check("指纹 相同配置得到相同指纹", fp1 == fp2, fp1);

            f2.PinyinSortName = !f1.PinyinSortName;
            var fp3 = StrmAssistant.ScheduledTask.MetadataEnhanceTask.ComputeOptionsFingerprint(f2);
            Check("指纹 开关变化后指纹不同", fp1 != fp3, fp1 + " vs " + fp3);
            Check("指纹 非功能开关不影响指纹",
                StrmAssistant.ScheduledTask.MetadataEnhanceTask.ComputeOptionsFingerprint(
                    new StrmAssistant.Options.MetadataEnhanceOptions { FullScanOnEveryRun = !f1.FullScanOnEveryRun }) == fp1);

            // TMDb 缓存：原子写入 + 半截文件识别
            var tmdbCacheDir = StrmAssistant.Common.TmdbCache.Directory;
            if (Directory.Exists(tmdbCacheDir)) Directory.Delete(tmdbCacheDir, true);

            StrmAssistant.Common.TmdbCache.Put("http://example.com/api/1", "{\"id\":1}");
            Check("TMDb 缓存 写入后可读回",
                StrmAssistant.Common.TmdbCache.TryGet("http://example.com/api/1")?.Contains("\"id\":1") == true);

            var keyFile = Directory.GetFiles(tmdbCacheDir, "*.json");
            Check("TMDb 缓存 没有遗留临时文件",
                Directory.GetFiles(tmdbCacheDir, "*.tmp").Length == 0, keyFile.Length + " json files");

            File.WriteAllText(keyFile[0], "{\"id\":");
            Check("TMDb 缓存拒绝半截文件",
                StrmAssistant.Common.TmdbCache.TryGet("http://example.com/api/1") == null);
            Check("TMDb 缓存半截文件已被清理",
                Directory.GetFiles(tmdbCacheDir, "*.json").Length == 0);

            if (Directory.Exists(tmdbCacheDir)) Directory.Delete(tmdbCacheDir, true);

            // 抑制指定插件更新：名单解析与过滤
            StrmAssistant.Common.PluginUpdateFilter.ParseNames("MovieDb, Tvdb");
            Check("PluginUpdateFilter 解析逗号与空格",
                StrmAssistant.Common.PluginUpdateFilter.ParseNames("MovieDb, Tvdb ; OMDb").Length == 3,
                string.Join("|", StrmAssistant.Common.PluginUpdateFilter.ParseNames("MovieDb, Tvdb ; OMDb")));
            Check("PluginUpdateFilter 空配置返回空数组",
                StrmAssistant.Common.PluginUpdateFilter.ParseNames("  ").Length == 0);

            var pkgMovieDb = new MediaBrowser.Model.Updates.PackageVersionInfo { name = "MovieDb", targetFilename = "MovieDb.dll" };
            var pkgTvdb = new MediaBrowser.Model.Updates.PackageVersionInfo { name = "Tvdb", targetFilename = "Tvdb.dll" };
            Check("PluginUpdateFilter 按包名匹配（忽略大小写）",
                StrmAssistant.Common.PluginUpdateFilter.ShouldSuppress(pkgMovieDb,
                    StrmAssistant.Common.PluginUpdateFilter.ParseNames("moviedb")));
            Check("PluginUpdateFilter 按文件名匹配（去 .dll）",
                StrmAssistant.Common.PluginUpdateFilter.ShouldSuppress(pkgTvdb,
                    StrmAssistant.Common.PluginUpdateFilter.ParseNames("Tvdb")));
            Check("PluginUpdateFilter 未命中不抑制",
                !StrmAssistant.Common.PluginUpdateFilter.ShouldSuppress(pkgTvdb,
                    StrmAssistant.Common.PluginUpdateFilter.ParseNames("MovieDb")));
            Check("PluginUpdateFilter 数组过滤只保留未抑制项",
                StrmAssistant.Common.PluginUpdateFilter.Apply(new[] { pkgMovieDb, pkgTvdb }, "MovieDb").Length == 1);

            // 多版本美化命名规则
            Check("多版本命名 4K", StrmAssistant.Common.MultiVersionNaming.QualityLabel(2160, null) == "4K");
            Check("多版本命名 1080p", StrmAssistant.Common.MultiVersionNaming.QualityLabel(1080, null) == "1080p");
            Check("多版本命名 720p", StrmAssistant.Common.MultiVersionNaming.QualityLabel(720, null) == "720p");
            Check("多版本命名 无高度时回退",
                StrmAssistant.Common.MultiVersionNaming.QualityLabel(null, 8000000) == "高码率");
            Check("多版本命名 去后缀",
                StrmAssistant.Common.MultiVersionNaming.StripSuffix("影片 - 1080p") == "影片");
            Check("多版本命名 去后缀（无后缀不变）",
                StrmAssistant.Common.MultiVersionNaming.StripSuffix("影片 (2024)") == "影片 (2024)");

            var names = StrmAssistant.Common.MultiVersionNaming.ComputeNames("影片",
                new[] { ("1", "1080p"), ("2", "720p") });
            Check("多版本命名 不同清晰度直接拼接",
                names["1"] == "影片 - 1080p" && names["2"] == "影片 - 720p");

            var dup = StrmAssistant.Common.MultiVersionNaming.ComputeNames("影片",
                new[] { ("1", "1080p"), ("2", "1080p") });
            Check("多版本命名 同清晰度自动编号",
                dup["1"] == "影片 - 1080p (1)" && dup["2"] == "影片 - 1080p (2)");

            // 关键词排除（指纹排除 / 媒体信息排除共用）
            Check("关键词排除 空关键词不排除",
                !StrmAssistant.Common.KeywordFilter.Hit("剧集 A", "/media/A.strm", ""));
            Check("关键词排除 命中名称",
                StrmAssistant.Common.KeywordFilter.Hit("特番 01", "/media/x.strm", "特番"));
            Check("关键词排除 命中路径",
                StrmAssistant.Common.KeywordFilter.Hit("第1集", "/media/花絮/1.strm", "花絮"));
            Check("关键词排除 多关键词任一命中",
                StrmAssistant.Common.KeywordFilter.Hit("SP 特别篇", "/media/x", "花絮, SP"));
            Check("关键词排除 大小写不敏感",
                StrmAssistant.Common.KeywordFilter.Hit("Sample File", "/x", "sample"));
            Check("关键词排除 未命中",
                !StrmAssistant.Common.KeywordFilter.Hit("正片 01", "/media/正片/01.strm", "花絮,预告"));
            Check("关键词排除 解析会去掉空项",
                StrmAssistant.Common.KeywordFilter.Parse(" a , ,b, ").Length == 2);

            // 多版本排序偏好
            var variants = new[] { ("b", 720L), ("a", 1080L), ("c", 2160L) };
            var high = StrmAssistant.Common.MultiVersionNaming.OrderVariants(variants, "HighToLowQuality", v => v.Item2, v => v.Item1);
            Check("多版本排序 高到低", high[0].Item1 == "c" && high[2].Item1 == "b");
            var low = StrmAssistant.Common.MultiVersionNaming.OrderVariants(variants, "LowToHighQuality", v => v.Item2, v => v.Item1);
            Check("多版本排序 低到高", low[0].Item1 == "b" && low[2].Item1 == "c");
            var asc = StrmAssistant.Common.MultiVersionNaming.OrderVariants(variants, "PathAscending", v => v.Item2, v => v.Item1);
            Check("多版本排序 路径升序", asc[0].Item1 == "a" && asc[2].Item1 == "c");
            var desc = StrmAssistant.Common.MultiVersionNaming.OrderVariants(variants, "PathDescending", v => v.Item2, v => v.Item1);
            Check("多版本排序 路径降序", desc[0].Item1 == "c" && desc[2].Item1 == "a");
            var def = StrmAssistant.Common.MultiVersionNaming.OrderVariants(variants, "SystemDefault", v => v.Item2, v => v.Item1);
            Check("多版本排序 默认保持原顺序", def[0].Item1 == "b" && def[1].Item1 == "a");

            // MDBList 响应解析
            var okJson = "{\"score\": 7.8, \"votes\": 1234}";
            var ok = StrmAssistant.Common.MdbListApi.Parse(okJson);
            Check("MDBList 解析 正常分数", ok.Ok && ok.Score == 7.8 && ok.Votes == 1234);
            var strJson = "{\"score\": \"6.5\"}";
            var strRes = StrmAssistant.Common.MdbListApi.Parse(strJson);
            Check("MDBList 解析 字符串分数", strRes.Ok && strRes.Score == 6.5);
            var noScore = "{\"title\": \"x\"}";
            Check("MDBList 解析 缺 score 判失败", !StrmAssistant.Common.MdbListApi.Parse(noScore).Ok);
            var badJson = "not-json";
            Check("MDBList 解析 非法 JSON 判失败", !StrmAssistant.Common.MdbListApi.Parse(badJson).Ok);
            Check("MDBList 解析 空响应判失败", !StrmAssistant.Common.MdbListApi.Parse("").Ok);

            // 命名幂等性：重复跑任务不能再往后加尾巴
            Check("多版本命名 剥离清晰度后缀",
                StrmAssistant.Common.MultiVersionNaming.StripSuffix("影片 - 1080p") == "影片");
            Check("多版本命名 剥离序号",
                StrmAssistant.Common.MultiVersionNaming.StripSuffix("影片 - 1080p (2)") == "影片");
            Check("多版本命名 剥离扩展标签",
                StrmAssistant.Common.MultiVersionNaming.StripSuffix("影片 - 720p H264 (1)") == "影片");
            var fnCase = StrmAssistant.Common.MultiVersionNaming.StripSuffix("影片 - 影片 (2024)");
            Check("多版本命名 剥离文件名标签", fnCase == "影片", "实际=\"" + fnCase + "\"");
            Check("多版本命名 保留正常破折号标题",
                StrmAssistant.Common.MultiVersionNaming.StripSuffix("Spider-Man - Homecoming") == "Spider-Man - Homecoming");
            var once = StrmAssistant.Common.MultiVersionNaming.StripSuffix("影片 - 720p H264 (1) - 影片 (2024)");
            var twice = StrmAssistant.Common.MultiVersionNaming.StripSuffix(once);
            Check("多版本命名 幂等", once == "影片" && twice == once);

            // ffprobe 兜底解析：生产上 我不是大师 4K HEVC MKV 因为缺容器被浏览器直接播放失败
            const string probeJson = @"{""streams"":[
 {""index"":0,""codec_name"":""hevc"",""profile"":""Main 10"",""codec_type"":""video"",""width"":3840,""height"":1920,""pix_fmt"":""yuv420p10le"",""color_transfer"":""smpte2084"",""avg_frame_rate"":""25/1"",""disposition"":{""default"":1},""tags"":{""language"":""chi""}},
 {""index"":1,""codec_name"":""eac3"",""codec_type"":""audio"",""channels"":6,""channel_layout"":""5.1(side)"",""sample_rate"":""48000"",""bit_rate"":""256000"",""disposition"":{""default"":1},""tags"":{""language"":""chi"",""title"":""Dolby Digital Plus""}},
 {""index"":2,""codec_name"":""aac"",""codec_type"":""audio"",""channels"":2,""disposition"":{""default"":0},""tags"":{""language"":""chi"",""title"":""AAC 320k"",""BPS"":""320000""}},
 {""index"":3,""codec_name"":""aac"",""codec_type"":""audio"",""channels"":2,""disposition"":{""default"":0}},
 {""index"":4,""codec_name"":""ttf"",""codec_type"":""attachment"",""tags"":{""filename"":""font.ttf""}},
 {""index"":5,""codec_type"":""data""}],
 ""format"":{""format_name"":""matroska,webm"",""duration"":""2730.847"",""size"":""1938468215"",""bit_rate"":""5678901""}}";
            var parsed = StrmAssistant.Common.StrmProbe.ParseFfprobeJson(probeJson,
                "http://192.168.1.10:5211/api/strm/play/1/x/n/%E6%88%91.mkv/s/y");
            var pv = parsed.MediaStreams.Find(x => x.Type == MediaBrowser.Model.Entities.MediaStreamType.Video);
            var pa = parsed.MediaStreams.FindAll(x => x.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio);
            Check("ffprobe 解析 容器 mkv", parsed.Container == "mkv", "实际=" + parsed.Container);
            Check("ffprobe 解析 大小/码率", parsed.Size == 1938468215L && parsed.Bitrate == 5678901);
            Check("ffprobe 解析 时长", parsed.RunTimeTicks == (long)(2730.847 * TimeSpan.TicksPerSecond));
            Check("ffprobe 解析 视频 10bit HDR", pv != null && pv.BitDepth == 10 && pv.ColorTransfer == "smpte2084" && pv.Profile == "Main 10");
            Check("ffprobe 解析 音轨数量与序号", pa.Count == 3 && pa[0].Index == 1 && pa[1].Index == 2 && pa[2].Index == 3);
            Check("ffprobe 解析 音轨标题/语言/默认", pa[0].Title == "Dolby Digital Plus" && pa[0].Language == "chi" &&
                                                 pa[0].IsDefault && !pa[1].IsDefault && pa[1].BitRate == 320000);
            Check("ffprobe 解析 data 流被丢弃、附件不当成内嵌图片",
                !parsed.MediaStreams.Exists(x => x.Index == 5) &&
                !parsed.MediaStreams.Exists(x => x.Type == MediaBrowser.Model.Entities.MediaStreamType.EmbeddedImage));
            Check("容器归一 mp4", StrmAssistant.Common.StrmProbe.NormalizeContainer("mov,mp4,m4a,3gp,3g2,mj2", "http://x/a.mp4") == "mp4");
            Check("容器归一 ts", StrmAssistant.Common.StrmProbe.NormalizeContainer("mpegts", "http://x/a.ts") == "ts");
            Check("容器归一 不会返回 strm", StrmAssistant.Common.StrmProbe.NormalizeContainer(null, "/media/a.strm") != "strm");

            // 单选/多选选项必须是 IsEnabled = true，否则 Emby 渲染成 disabled，点不动
            Check("下拉可点 版本名称", AllEnabled(new StrmAssistant.Options.ExperienceEnhanceOptions(), "VersionNameList"));
            Check("下拉可点 版本顺序", AllEnabled(new StrmAssistant.Options.ExperienceEnhanceOptions(), "VersionSortList"));
            Check("下拉可点 中文搜索模式", AllEnabled(new StrmAssistant.Options.ExperienceEnhanceOptions(), "SearchModeList"));
            Check("下拉可点 截图处理", AllEnabled(new StrmAssistant.Options.MediaInfoExtractOptions(), "ImageCapturePostProcessModeList"));
            Check("下拉可点 媒体信息 JSON", AllEnabled(new StrmAssistant.Options.MediaInfoExtractOptions(), "PersistMediaInfoModeList"));
            Check("下拉可点 豆瓣作用于", AllEnabled(new StrmAssistant.Options.MetadataEnhanceOptions(), "DoubanAssistScrapeScopeList"));
            {
                // 老配置文件里存的是 IsEnabled=false 的列表：读出来时要被修正
                object mo = new StrmAssistant.Options.MediaInfoExtractOptions();
                var prop = mo.GetType().GetProperty("ExclusiveControlList");
                var stale = Activator.CreateInstance(prop.PropertyType);
                var opt = Activator.CreateInstance(prop.PropertyType.GetGenericArguments()[0]);
                opt.GetType().GetProperty("Value").SetValue(opt, "IgnoreFileChange");
                opt.GetType().GetProperty("IsEnabled").SetValue(opt, false);
                prop.PropertyType.GetMethod("Add").Invoke(stale, new[] { opt });
                prop.SetValue(mo, stale);
                Check("旧配置里的 disabled 选项读出时被修正", AllEnabled(mo, "ExclusiveControlList"));
            }

            // 代理：协议看地址前缀
            {
                Uri u;
                Check("代理 http", StrmAssistant.Common.ProxyConfig.TryParse("http://127.0.0.1:7890", out u) && u.Scheme == "http" && u.Port == 7890);
                Check("代理 socks5", StrmAssistant.Common.ProxyConfig.TryParse("socks5://10.0.0.2:1080", out u) && u.Scheme == "socks5");
                Check("代理 socks5h 当 socks5", StrmAssistant.Common.ProxyConfig.TryParse("socks5h://10.0.0.2:1080", out u) && u.Scheme == "socks5");
                Check("代理 不写协议按 http", StrmAssistant.Common.ProxyConfig.TryParse("192.168.1.10:7890", out u) && u.Scheme == "http" && u.Host == "192.168.1.10");
                Check("代理 空 / 乱写 不接受", !StrmAssistant.Common.ProxyConfig.TryParse("", out u) && !StrmAssistant.Common.ProxyConfig.TryParse("ftp://x:1", out u));
                var wp = StrmAssistant.Common.ProxyConfig.CreateWebProxy(new Uri("http://1.2.3.4:7890"));
                Check("代理 局域网地址直连", wp.IsBypassed(new Uri("http://192.168.1.10:3000/api")) && wp.IsBypassed(new Uri("http://127.0.0.1:8096/")) &&
                                       wp.IsBypassed(new Uri("http://172.20.0.5/")) && !wp.IsBypassed(new Uri("https://api.themoviedb.org/3")));

                var g = new StrmAssistant.Options.GeneralOptions { ProxyUrl = "http://127.0.0.1:1080", ProxyMode = "Socks5" };
                Check("旧配置 SOCKS5 单选写进地址", StrmAssistant.Common.ProxyConfig.MigrateLegacy(g) && g.ProxyUrl == "socks5://127.0.0.1:1080" && g.ProxyMode == "Http");
                Check("旧配置 已迁移不再改动", !StrmAssistant.Common.ProxyConfig.MigrateLegacy(g));
                var h = new StrmAssistant.Options.GeneralOptions { ProxyUrl = "http://127.0.0.1:7890", ProxyMode = "Http" };
                Check("旧配置 HTTP 不动", !StrmAssistant.Common.ProxyConfig.MigrateLegacy(h) && h.ProxyUrl == "http://127.0.0.1:7890");
            }

            // 重复的刷新规则并入「刷新哪些分集」
            {
                var po = new StrmAssistant.Options.PluginOptions();
                po.MetadataEnhanceOptions.MetadataRefreshPreferences = "NoOverview,NonChineseNameOverview";
                po.MetadataEnhanceOptions.EpisodeRefreshScope = "NoImage";
                var changed = StrmAssistant.Plugin.MigrateLegacyOptions(po);
                Check("刷新规则迁移", changed && po.MetadataEnhanceOptions.EpisodeRefreshScope == "NoOverview,NonChineseOverview" &&
                                     po.MetadataEnhanceOptions.MetadataRefreshPreferences == "", po.MetadataEnhanceOptions.EpisodeRefreshScope);
            }

            // 更新插件：版本号、选包、解压
            {
                var T = typeof(StrmAssistant.ScheduledTask.UpdatePluginTask);
                Check("更新 版本号解析", StrmAssistant.ScheduledTask.UpdatePluginTask.ParseVersion("v2.2.0") == new Version(2, 2, 0) &&
                                       StrmAssistant.ScheduledTask.UpdatePluginTask.ParseVersion("垃圾") == null);
                Check("更新 2.2.0 比 2.1.0.0 新、与 2.2.0.0 相同",
                    StrmAssistant.ScheduledTask.UpdatePluginTask.IsNewer(new Version(2, 1, 0, 0), new Version(2, 2, 0)) &&
                    !StrmAssistant.ScheduledTask.UpdatePluginTask.IsNewer(new Version(2, 2, 0, 0), new Version(2, 2, 0)));
                var assets = new System.Collections.Generic.List<StrmAssistant.ScheduledTask.UpdatePluginTask.ApiAssetInfo>
                {
                    new() { name = "StrmAssistantLite-emby4.8.zip", browser_download_url = "https://x/a48.zip" },
                    new() { name = "StrmAssistantLite-emby4.9.zip", browser_download_url = "https://x/a49.zip" },
                    new() { name = "StrmAssistantLite-emby4.10.zip", browser_download_url = "https://x/a410.zip" },
                };
                Check("更新 按 Emby 版本选包", StrmAssistant.ScheduledTask.UpdatePluginTask.PickAsset(assets, new Version(4, 10, 0, 40))?.name == "StrmAssistantLite-emby4.10.zip" &&
                                         StrmAssistant.ScheduledTask.UpdatePluginTask.PickAsset(assets, new Version(4, 9, 1, 80))?.name == "StrmAssistantLite-emby4.9.zip" &&
                                         StrmAssistant.ScheduledTask.UpdatePluginTask.PickAsset(assets, new Version(4, 1, 0))?.name == null);
                assets.Add(new() { name = "StrmAssistantLite.zip", browser_download_url = "https://x/all.zip" });
                Check("更新 直接下载的文件名顺序", string.Join(",", StrmAssistant.ScheduledTask.UpdatePluginTask.AssetCandidates(new Version(4, 10, 0, 40))) ==
                                                "StrmAssistantLite-emby4.10.zip,StrmAssistantLite.zip");
                Check("更新 没有对应版本时用通用包", StrmAssistant.ScheduledTask.UpdatePluginTask.PickAsset(assets, new Version(4, 11, 0))?.name == "StrmAssistantLite.zip");

                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sa-upd-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "StrmAssistantLite.dll"), "old");
                var ms = new System.IO.MemoryStream();
                using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    void Add(string n, string c) { using var w = new System.IO.StreamWriter(z.CreateEntry(n).Open()); w.Write(c); }
                    Add("StrmAssistantLite.dll", "new"); Add("StrmAssistantDeps/0Harmony.dll", "h"); Add("zh/StrmAssistantLite.resources.dll", "z");
                    Add("../evil.txt", "x"); Add("other.dll", "x");
                }
                var n = StrmAssistant.ScheduledTask.UpdatePluginTask.Install(ms.ToArray(), dir);
                Check("更新 解压覆盖、只写允许的文件", n == 3 && System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "StrmAssistantLite.dll")) == "new" &&
                                               System.IO.File.Exists(System.IO.Path.Combine(dir, "StrmAssistantDeps", "0Harmony.dll")) &&
                                               !System.IO.File.Exists(System.IO.Path.Combine(dir, "other.dll")) &&
                                               !System.IO.File.Exists(System.IO.Path.Combine(dir, "..", "evil.txt")), "写入 " + n);
                System.IO.Directory.Delete(dir, true);
            }

            // 弹窗 ⇄ 设置：按名字复制；弹窗回写时不回写下拉列表
            {
                var live = new StrmAssistant.Options.IntroSkipOptions { MaxIntroDurationSeconds = 120, ClientScope = "Emby" };
                live.LibraryList = new[] { new Emby.Web.GenericEdit.Common.EditorSelectOption { Value = "1", Name = "剧集", IsEnabled = true } };
                var dlg = new StrmAssistant.Options.Dialogs.IntroSkipRulesDialog();
                StrmAssistant.Options.UI.OptionCopy.CopyAll(live, dlg);
                Check("弹窗打开时带入当前值和列表", dlg.MaxIntroDurationSeconds == 120 && dlg.ClientScope == "Emby" && dlg.LibraryList != null);
                dlg.MaxIntroDurationSeconds = 200; dlg.LibraryList = null;
                StrmAssistant.Options.UI.OptionCopy.CopyEditable(dlg, live);
                Check("弹窗保存写回值、不清空列表", live.MaxIntroDurationSeconds == 200 && live.LibraryList != null);
                var mem = new StrmAssistant.Options.MemoryOptions { MemoryUsageLimitMB = 2048 };
                StrmAssistant.Options.UI.OptionCopy.CopyEditable(new StrmAssistant.Options.Dialogs.MemoryGuardDialog { MemoryUsageLimitMB = null }, mem);
                Check("弹窗能把内存上限清空", mem.MemoryUsageLimitMB == null);
            }

            // ---- 多版本：版本下拉框标签 / 排序 / 旧标题还原 ----
            {
                var N = typeof(StrmAssistant.Common.MultiVersionNaming);
                Check("版本标签 媒体信息 4K HDR10",
                    StrmAssistant.Common.MultiVersionNaming.VersionLabel("MediaInfo", 2160, null, "hevc", "HDR10", "HDR 10", "eac3", 6, null, null) == "4K HDR10");
                Check("版本标签 媒体信息 SDR 不带范围",
                    StrmAssistant.Common.MultiVersionNaming.VersionLabel("MediaInfo", 1080, null, "h264", "None", "SDR", null, null, null, null) == "1080p");
                var ext = StrmAssistant.Common.MultiVersionNaming.VersionLabel("MediaInfoExtended", 2160, null, "hevc", "DolbyVision", null, "eac3", 6, 27165933568L, null);
                Check("版本标签 详细", ext == "4K HEVC DV · EAC3 5.1 · 25.3GB", ext);
                Check("版本标签 无媒体信息时保持原名",
                    StrmAssistant.Common.MultiVersionNaming.VersionLabel("MediaInfo", null, null, null, null, null, null, null, null, null) == null);
                Check("版本标签 跟随系统默认不改",
                    StrmAssistant.Common.MultiVersionNaming.VersionLabel("SystemDefault", 2160, null, "hevc", null, null, null, null, null, null) == null);
                var dd = StrmAssistant.Common.MultiVersionNaming.Dedupe(new[] { "1080p", "4K", "1080p" });
                Check("版本标签 同名追加序号", dd[0] == "1080p" && dd[1] == "4K" && dd[2] == "1080p (2)", string.Join("|", dd));

                string R(string name, params string[] files) => StrmAssistant.Common.MultiVersionNaming.RestoreLegacyTitle(name, files);
                Check("旧标题还原 叠了多层的",
                    R("Hook Test - 未知 - 未知 - 未知 - 未知 - 未知 - Hook Test (2024) (1)", "Hook Test (2024)", "Hook Test (2024)") == "Hook Test");
                Check("旧标题还原 文件名里带 - 的",
                    R("Merge Test - 未知 - 未知 - Merge Test (2024) - 720p", "Merge Test (2024) - 720p", "Merge Test (2024) - 1080p") == "Merge Test");
                Check("旧标题还原 清晰度+编码+序号", R("盗梦空间 - 720p H264 (1)", "a") == "盗梦空间" && R("星际穿越 - 4K", "a") == "星际穿越");
                Check("旧标题还原 文件名标签", R("星际穿越 - Interstellar.2014.2160p", "Interstellar.2014.2160p", "inter.720p") == "星际穿越");
                Check("旧标题还原 正常标题不动",
                    R("Spider-Man - Far From Home", "x") == null && R("星际穿越", "x") == null && R("Blade Runner 2049", "x") == null
                    && R("Rocky (2006)", "x") == null);

                MediaBrowser.Model.Dto.MediaSourceInfo Src(string name, int h, string path) => new MediaBrowser.Model.Dto.MediaSourceInfo
                {
                    Name = name, Path = path,
                    MediaStreams = new System.Collections.Generic.List<MediaBrowser.Model.Entities.MediaStream>
                        { new MediaBrowser.Model.Entities.MediaStream { Type = MediaBrowser.Model.Entities.MediaStreamType.Video, Height = h, Codec = "h264" } }
                };
                var list = new System.Collections.Generic.List<MediaBrowser.Model.Dto.MediaSourceInfo>
                    { Src("a.720p", 720, "/m/a.mp4"), Src("b.2160p", 2160, "/m/b.mp4"), Src("c.1080p", 1080, "/m/c.mp4") };
                var outList = StrmAssistant.Common.Hooks.MultiVersionDisplayPatch.Apply(list, "MediaInfo", "HighToLowQuality", s => System.IO.Path.GetFileNameWithoutExtension(s.Path));
                Check("版本下拉框 改名+高到低排序", string.Join(",", outList.Select(x => x.Name)) == "4K,1080p,720p", string.Join(",", outList.Select(x => x.Name)));
                var list2 = new System.Collections.Generic.List<MediaBrowser.Model.Dto.MediaSourceInfo>
                    { Src("a", 720, "/m/z.mp4"), new MediaBrowser.Model.Dto.MediaSourceInfo { Name = "b", Path = "/m/a.mp4" } };
                var out2 = StrmAssistant.Common.Hooks.MultiVersionDisplayPatch.Apply(list2, "MediaInfo", "PathAscending", s => System.IO.Path.GetFileNameWithoutExtension(s.Path));
                Check("版本下拉框 有版本缺媒体信息时不改名，但仍排序", string.Join(",", out2.Select(x => x.Name)) == "b,a", string.Join(",", out2.Select(x => x.Name)));
                var list3 = new System.Collections.Generic.List<MediaBrowser.Model.Dto.MediaSourceInfo> { Src("x", 720, "/m/x.mp4"), Src("y", 1080, "/m/y.mp4") };
                var out3 = StrmAssistant.Common.Hooks.MultiVersionDisplayPatch.Apply(list3, "SystemDefault", "SystemDefault", null);
                Check("版本下拉框 全部跟随系统默认时原样", string.Join(",", out3.Select(x => x.Name)) == "x,y");
            }

            // 直连 / 预加载 / 通知（2026-11-05）
            {
                var strmDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sa-strm-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(strmDir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(strmDir, "a.strm"), "\n  http://x:5211/api/strm/play/1/abc  \n");
                System.IO.File.WriteAllText(System.IO.Path.Combine(strmDir, "b.strm"), "/mnt/local/b.mkv");
                Check("直连 读 strm 里的 http 地址", StrmAssistant.Common.Hooks.StrmDirectLinkPatch.ReadHttpStrmTarget(System.IO.Path.Combine(strmDir, "a.strm")) == "http://x:5211/api/strm/play/1/abc"
                    && StrmAssistant.Common.Hooks.StrmDirectLinkPatch.ReadHttpStrmTarget(System.IO.Path.Combine(strmDir, "b.strm")) == null
                    && StrmAssistant.Common.Hooks.StrmDirectLinkPatch.ReadHttpStrmTarget(System.IO.Path.Combine(strmDir, "c.mkv")) == null);
                System.IO.Directory.Delete(strmDir, true);
                Check("直连 黑名单匹配客户端名或 UA", StrmAssistant.Common.Hooks.StrmDirectLinkPatch.IsBlacklisted("infuse; senplayer", "Infuse-Direct", "")
                    && StrmAssistant.Common.Hooks.StrmDirectLinkPatch.IsBlacklisted("vidhub", "", "VidHub/1.2")
                    && !StrmAssistant.Common.Hooks.StrmDirectLinkPatch.IsBlacklisted("infuse", "Emby Web", "Chrome")
                    && !StrmAssistant.Common.Hooks.StrmDirectLinkPatch.IsBlacklisted("", "Infuse", ""));

                MediaBrowser.Controller.Entities.TV.Episode Ep(int s, int e) => new MediaBrowser.Controller.Entities.TV.Episode { ParentIndexNumber = s, IndexNumber = e };
                var eps = new[] { Ep(1, 1), Ep(1, 2), Ep(1, 3), Ep(2, 1), Ep(0, 1) };
                var n1 = StrmAssistant.Common.MediaInfoPrefetch.PickNext(eps, 1, 2);
                var n2 = StrmAssistant.Common.MediaInfoPrefetch.PickNext(eps, 1, 3);
                var n3 = StrmAssistant.Common.MediaInfoPrefetch.PickNext(eps, 2, 1);
                Check("预加载 下一集 / 跨季 / 最后一集", n1?.IndexNumber == 3 && n1?.ParentIndexNumber == 1 && n2?.ParentIndexNumber == 2 && n2?.IndexNumber == 1 && n3 == null);

                var mpData = StrmAssistant.Common.TaskReporter.BuildWebhookData("提取媒体信息", "完成 3 项");
                Check("通知 MoviePilot 格式", mpData.Contains("\"Event\":\"system.notificationtest\"") && mpData.Contains("\"Overview\":\"提取媒体信息：完成 3 项\"")
                    && mpData.Contains("\"User\":{\"Name\":\"神医助手\"}"), mpData);
            }

            // 对抗审查修复（2026-11-05）
            {
                var a = VersionTags.Of("The.Lord.of.the.Rings.The.Rings.of.Power.S01E01.2160p.AMZN.WEB-DL.DDP5.1.HDR.H.265");
                var b = VersionTags.Of("The.Lord.of.the.Rings.The.Rings.of.Power.S01E02.1080p.AMZN.WEB-DL.DDP5.1.H.264");
                var c = VersionTags.Of("The.Lord.of.the.Rings.The.Rings.of.Power.S01E03.2160p.AMZN.WEB-DL.DDP5.1.HDR.x265");
                Check("删除整季 不同版本标签不同", !a.SetEquals(b), string.Join(",", a) + " vs " + string.Join(",", b));
                Check("删除整季 同版本标签相同（H.265 = x265）", a.SetEquals(c), string.Join(",", a) + " vs " + string.Join(",", c));
                Check("相似度本身确实会误判（> 0.92）", CommonUtility.LevenshteinDistance(
                    LanguageUtility.CleanEpisodeName("The.Lord.of.the.Rings.The.Rings.of.Power.S01E01.2160p.AMZN.WEB-DL.DDP5.1.HDR.H.265"),
                    LanguageUtility.CleanEpisodeName("The.Lord.of.the.Rings.The.Rings.of.Power.S01E02.1080p.AMZN.WEB-DL.DDP5.1.H.264")) > 0.85);

                Check("深度删除 本地 strm 目标", StrmAssistant.Common.Hooks.DeepDeletePatch.ParseLocalStrmTarget("/mnt/media/a.mkv") == "/mnt/media/a.mkv"
                    && StrmAssistant.Common.Hooks.DeepDeletePatch.ParseLocalStrmTarget("file:///mnt/media/a.mkv") == "/mnt/media/a.mkv");
                Check("深度删除 网址不处理", StrmAssistant.Common.Hooks.DeepDeletePatch.ParseLocalStrmTarget("http://x/a.mkv") == null
                    && StrmAssistant.Common.Hooks.DeepDeletePatch.ParseLocalStrmTarget("relative/a.mkv") == null);
                Check("深度删除 附属文件只认字幕/nfo/图片", StrmAssistant.Common.Hooks.DeepDeletePatch.IsSidecarOf("Movie.zh.srt", "Movie")
                    && StrmAssistant.Common.Hooks.DeepDeletePatch.IsSidecarOf("Movie-poster.jpg", "Movie")
                    && !StrmAssistant.Common.Hooks.DeepDeletePatch.IsSidecarOf("Movie-1080p.mkv", "Movie")
                    && !StrmAssistant.Common.Hooks.DeepDeletePatch.IsSidecarOf("Movie2.srt", "Movie"));

                Check("标题修复 不动本来就带序号的标题", MultiVersionNaming.RestoreLegacyTitle("The Best of Both Worlds (1)", new[] { "x" }) == null);
                Check("标题修复 不截 4K Remaster", MultiVersionNaming.RestoreLegacyTitle("Alien - 4K Remaster", new string[0]) == null);
                Check("标题修复 旧尾巴照样还原", MultiVersionNaming.RestoreLegacyTitle("Hook Test - 未知 - 未知 - Hook Test (2024) (1)", new[] { "Hook Test (2024)" }) == "Hook Test"
                    && MultiVersionNaming.RestoreLegacyTitle("星际穿越 - 4K HDR10", new string[0]) == "星际穿越");

                Check("标记前集 特别篇不跟正片", !EpisodeProgressPolicy.IsBefore(0, 1, 1, 5) && !EpisodeProgressPolicy.IsBefore(null, 1, 1, 5));
                Check("标记前集 正片照常", EpisodeProgressPolicy.IsBefore(1, 4, 1, 5) && EpisodeProgressPolicy.IsBefore(1, 9, 2, 1)
                    && EpisodeProgressPolicy.IsBefore(0, 1, 0, 2));
            }

            // 中文搜索：模糊 + 拼音
            {
                int S(string title, string q) => PinyinSearch.Score(title, q);
                Check("模糊 中间子串 穿越", S("星际穿越", "穿越") == 80);
                Check("模糊 跨标点 哈利波特", S("哈利·波特与魔法石", "哈利波特") == 90);
                Check("模糊 全角冒号 阿凡达水之道", S("阿凡达：水之道", "阿凡达水之道") == 100 && S("阿凡达：水之道", "凡达水") == 80, PinyinSearch.Normalize("阿凡达：水之道") + " / " + PinyinSearch.Normalize("阿凡达水之道") + " / " + S("阿凡达：水之道", "阿凡达水之道"));
                Check("模糊 多词乱序", S("哈利·波特与魔法石", "魔法石 哈利") > 0);
                Check("模糊 多词有一个不中", S("哈利·波特与魔法石", "魔法石 阿凡达") == 0);
                Check("模糊 繁体查简体", S("复仇者联盟", "復仇者") == 90);
                Check("模糊 简体查繁体", S("復仇者聯盟", "联盟") == 80);
                Check("模糊 完全相同 100", S("星际穿越", "星际穿越") == 100);
                Check("拼音 首字母", S("星际穿越", "xjcy") == 70, S("星际穿越", "xjcy").ToString());
                Check("拼音 全拼", S("星际穿越", "xingjichuanyue") == 70);
                Check("拼音 混写", S("星际穿越", "xingjcy") == 70 && S("星际穿越", "星际cy") == 70);
                Check("拼音 大写和空格", S("星际穿越", "XingJi ChuanYue") > 0);
                Check("拼音 从中间开始", S("星际穿越", "chuanyue") == 60 && S("长安十二时辰", "sesc") == 60);
                Check("拼音 打字到一半", S("星际穿越", "xingjichuan") == 70);
                Check("拼音 多音字 长(chang)", S("长安十二时辰", "changan") == 70 && S("长安十二时辰", "cash") == 70);
                Check("拼音 多音字 重(chong)", S("重庆森林", "chongqing") == 70 && S("重庆森林", "cqsl") == 70);
                Check("拼音 多音字 行(hang/xing)", S("银行家", "yinhang") == 70 && S("行尸走肉", "xszr") == 70);
                Check("拼音 繁体标题", S("復仇者聯盟", "fczlm") == 70);
                Check("拼音 带英文的标题", S("黑客帝国 The Matrix", "hkdg") == 70 && S("黑客帝国 The Matrix", "matrix") == 80);
                Check("拼音 不相干", S("星际穿越", "abc") == 0 && S("星际穿越", "xjcz") == 0);
                Check("拼音 单字母不做", S("星际穿越", "x") == 0);
                Check("模糊 太短不做", !PinyinSearch.Worth(PinyinSearch.Keywords("x")) && PinyinSearch.Worth(PinyinSearch.Keywords("xj")));
                Check("搜索范围 合集=BoxSet", StrmAssistant.Common.Hooks.SearchEnhancementPatch.ScopeMatches("Collection", "BoxSet")
                    && StrmAssistant.Common.Hooks.SearchEnhancementPatch.ScopeMatches("Movie", "Movie")
                    && !StrmAssistant.Common.Hooks.SearchEnhancementPatch.ScopeMatches("Movie", "Series"));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var titles = Enumerable.Range(0, 20000).Select(i => "测试标题第" + i + "部长安十二时辰重庆森林").Select(PinyinSearch.Normalize).ToArray();
                var masks = titles.Select(PinyinSearch.LetterMask).ToArray();
                var kw = PinyinSearch.Keywords("zqsl");
                var kwm = kw.Select(PinyinSearch.KeywordMask).ToArray();
                sw.Restart();
                var n = 0;
                for (var i = 0; i < titles.Length; i++) if (PinyinSearch.Score(titles[i], masks[i], kw, kwm) > 0) n++;
                Check("拼音 2 万条目扫描 < 300ms", n == 20000 && sw.ElapsedMilliseconds < 300, sw.ElapsedMilliseconds + "ms, hits " + n);
            }

            Check("剧集跨库合并 去掉库 id", StrmAssistant.Common.Hooks.SeriesMergePatch.StripLibraries("888001-zh-CN-12-34", new[] { "12", "34" }) == "888001-zh-CN"
                && StrmAssistant.Common.Hooks.SeriesMergePatch.StripLibraries("888001-zh-CN", new string[0]) == "888001-zh-CN"
                && StrmAssistant.Common.Hooks.SeriesMergePatch.StripLibraries("888001-99", new[] { "12" }) == "888001-99");

            Console.WriteLine();
            Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
