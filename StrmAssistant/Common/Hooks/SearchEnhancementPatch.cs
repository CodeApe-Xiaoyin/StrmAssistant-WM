using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Querying;
using StrmAssistant.Options;
// 4.8 没有 IDataContext（GetItemsResult 只有 (InternalItemsQuery) 一个重载，每次查询自己开连接），
// 4.9+ 才有。插件按 4.8 接口编译，所以这里一律当 object 传，用到时反射调用。
using DataContext = System.Object;

namespace StrmAssistant.Common.Hooks
{
    /// <summary>
    /// 「中文搜索增强」，在 Emby 查询之后补三样：
    ///   · 简繁互搜：用查询词的简繁变体再查一遍；
    ///   · 模糊搜索：标题任意位置包含查询词（忽略空格、标点、简繁、大小写；空格分开的多个词都要命中）；
    ///   · 拼音搜索：全拼、首字母、混写，支持常见多音字。
    /// 后两项靠 <see cref="Common.SearchIndex"/> 在内存里算出候选 id，再按 id 用原查询的条件取条目。
    /// </summary>
    public static class SearchEnhancementPatch
    {
        // 重入保护：补丁里自己再调 QueryItems 时不能再进补丁，否则无限递归。
        // 以前用进程级信号量：同一时刻别的查询（别的用户、搜索页同时发出的几路查询、后台任务）
        // 在跑时，当前搜索的增强会被直接跳过——实测并发 30 次只有 8 次生效。
        // AsyncLocal 跟着调用链走（切线程也带着），只挡住自己的嵌套调用，不影响别人。
        private static readonly System.Threading.AsyncLocal<bool> InPatch = new System.Threading.AsyncLocal<bool>();

        public static bool Installed { get; private set; }

        public static string TryInstall()
        {
            if (Installed) return "already";

            try
            {
                var target = Type.GetType("Emby.Server.Implementations.Library.LibraryManager, Emby.Server.Implementations");
                if (target == null) return "找不到 LibraryManager";

                var postfix = typeof(SearchEnhancementPatch).GetMethod(nameof(Postfix),
                    BindingFlags.NonPublic | BindingFlags.Static);

                var total = 0;
                var messages = new System.Collections.Generic.List<string>();

                // 4.9 / 4.10 有 GetItemsResult(query, IDataContext, CancellationToken)：只挂这个最内层的，
                // QueryItems 和其它 GetItemsResult 重载最终都走到它，挂一处就够，也不会同一次查询被增强两遍。
                // 4.8 没有 IDataContext，只有 GetItemsResult(InternalItemsQuery)，QueryItems 也走它。
                var hasDataContext = target.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Any(m => m.Name == "GetItemsResult" && m.GetParameters().Any(IsDataContext));
                var r = HarmonyHelper.PatchPostfix(target, "GetItemsResult", postfix,
                    "StrmAssistant.SearchEnhancement.GetItemsResult",
                    hasDataContext
                        ? (Func<MethodInfo, bool>)(m => m.GetParameters().Any(IsDataContext))
                        : m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(InternalItemsQuery),
                    out var patched);
                total += patched;
                messages.Add((hasDataContext ? "GetItemsResult(dataContext):" : "GetItemsResult(query):") +
                             (patched > 0 ? "ok(" + patched + ")" : "none"));

                Installed = total > 0;
                return string.Join(" | ", messages);
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                return "失败: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        private static bool IsDataContext(ParameterInfo p) => p.ParameterType.Name == "IDataContext";

        /// <summary>两种挂点共用：4.8 的参数是 (query)，4.9+ 是 (query, dataContext, cancellationToken)。</summary>
        private static void Postfix(object[] __args, ref QueryResult<BaseItem> __result)
        {
            if (__args == null || __args.Length == 0 || !(__args[0] is InternalItemsQuery query)) return;
            var dataContext = __args.Length > 1 ? __args[1] : null;
            var token = __args.Length > 2 && __args[2] is System.Threading.CancellationToken t ? t : System.Threading.CancellationToken.None;
            Enhance(query, dataContext, token, ref __result);
        }

        private static MethodInfo _getItemsWithContext;

        private static QueryResult<BaseItem> RunQuery(InternalItemsQuery q, DataContext dataContext,
            System.Threading.CancellationToken cancellationToken)
        {
            if (dataContext != null)
            {
                // 4.9+：沿用原查询的数据库上下文（同一个连接 / 事务）
                _getItemsWithContext ??= Plugin.LibraryManager.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "GetItemsResult" && m.GetParameters().Length == 3 &&
                                         m.GetParameters()[0].ParameterType == typeof(InternalItemsQuery) &&
                                         IsDataContext(m.GetParameters()[1]));
                if (_getItemsWithContext != null)
                    return (QueryResult<BaseItem>)_getItemsWithContext.Invoke(Plugin.LibraryManager,
                        new[] { q, dataContext, cancellationToken });
            }

            return Plugin.LibraryManager.GetItemsResult(q);
        }

        private static void Enhance(InternalItemsQuery query, DataContext dataContext,
            System.Threading.CancellationToken cancellationToken, ref QueryResult<BaseItem> __result)
        {
            if (InPatch.Value) return;

            try
            {
                InPatch.Value = true;

                var options = Plugin.Instance?.CurrentOptions?.ExperienceEnhanceOptions;
                if (options?.EnhanceChineseSearch != true) return;

                var term = query?.SearchTerm;
                if (string.IsNullOrWhiteSpace(term)) return;

                // 翻页请求（StartIndex > 0）一律不补：补充结果只放在第一页，否则每一页都会重复追加同一批条目
                if (query.StartIndex.HasValue && query.StartIndex.Value > 0) return;
                var limit = query.Limit.HasValue && query.Limit.Value > 0 ? query.Limit : null;

                // 模式差异：
                //   Lite = 只在零结果时用简繁变体 / 模糊 / 拼音兜底
                //   Full = 有结果时也把它们命中的条目追加在后面
                var fullMode = string.Equals(options.EnhanceChineseSearchMode, "Full",
                    StringComparison.OrdinalIgnoreCase);

                // 「排除原名匹配」：名称里不含查询词、但靠着原名命中的条目要去掉
                if (options.HasSearchTuning(ExperienceEnhanceOptions.SearchTuningOption.ExcludeOriginalTitle) &&
                    __result?.Items != null && __result.Items.Length > 0)
                {
                    // 名称是否命中：用和模糊搜索同一套规则（忽略大小写、标点、简繁，多个词顺序不限）。
                    // 以前按字面子串比，「test hook」会把 Emby 正确命中的「Hook Test」删掉。
                    var kept = __result.Items
                        .Where(i => (i.Name ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    Common.PinyinSearch.Score(i.Name, term) > 0)
                        .ToArray();

                    if (kept.Length != __result.Items.Length)
                    {
                        var removed = __result.Items.Length - kept.Length;
                        Plugin.Instance?.Logger?.Info("ChineseSearch - 排除原名匹配 " + removed + " 个条目（查询「" + term + "」）");
                        // 总数减去去掉的个数（以前直接用本页条数覆盖，客户端会以为没有下一页）
                        __result = new QueryResult<BaseItem>
                        {
                            Items = kept,
                            TotalRecordCount = Math.Max(kept.Length, __result.TotalRecordCount - removed)
                        };
                    }

                    if (kept.Length > 0 && !fullMode) return;
                }

                if (!fullMode && __result?.Items != null && __result.Items.Length > 0) return;

                var both = options.HasSearchTuning(ExperienceEnhanceOptions.SearchTuningOption.MixedChineseVariants);
                var onlySimplified = options.HasSearchTuning(ExperienceEnhanceOptions.SearchTuningOption.TraditionalToSimplifiedChinese);
                var noTuning = !both && !onlySimplified &&
                               (options.SearchTuningPreferences ?? string.Empty).Trim().Length == 0;

                var variants = new System.Collections.Generic.List<string>();
                if (both || noTuning) variants.Add(Common.ChineseTextHelper.ToTraditional(term));
                if (both || onlySimplified || noTuning) variants.Add(Common.ChineseTextHelper.ToSimplified(term));

                variants = variants
                    .Where(v => !string.IsNullOrWhiteSpace(v) && !string.Equals(v, term, StringComparison.Ordinal))
                    .Distinct()
                    .ToList();

                var scope = (options.SearchScope ?? string.Empty)
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

                foreach (var variant in variants)
                {
                    var clone = CloneWithTerm(query, variant);
                    // 必须沿用调用方的 dataContext（已经打开的数据库连接）：
                    // 另开连接的话，并发搜索时每个请求都攥着一个连接再等第二个，连接池耗尽后整个实例的查询全部卡死
                    // （2026-11-05 在测试实例上用 30 个并发搜索复现过）。
                    var alt = RunQuery(clone, dataContext, cancellationToken);
                    var variantHits = FilterScope(alt?.Items, scope);
                    if (variantHits.Length == 0) continue;

                    if (Merge(ref __result, variantHits, fullMode, limit))
                    {
                        Plugin.Instance?.Logger?.Info("ChineseSearch - 「" + term + "」用简繁变体「" + variant + "」补充 " +
                                                      variantHits.Length + " 个条目");
                        if (!fullMode) return;
                    }
                }

                // 模糊 + 拼音：Emby 只按词的开头匹配，「穿越」搜不到「星际穿越」，拼音更是完全不支持。
                if (!fullMode && __result?.Items != null && __result.Items.Length > 0) return;

                var excludeOriginal = options.HasSearchTuning(ExperienceEnhanceOptions.SearchTuningOption.ExcludeOriginalTitle);
                var hits = Common.SearchIndex.Find(term, includeOriginal: !excludeOriginal);
                if (hits.Length == 0) return;

                var existing = new System.Collections.Generic.HashSet<long>(
                    (__result?.Items ?? Array.Empty<BaseItem>()).Select(i => i.InternalId));
                var restrict = query.ItemIds != null && query.ItemIds.Length > 0
                    ? new System.Collections.Generic.HashSet<long>(query.ItemIds)
                    : null;

                var candidates = hits
                    .Where(h => !existing.Contains(h.Id) && (restrict == null || restrict.Contains(h.Id)))
                    .ToList();
                if (candidates.Count == 0) return;

                var byId = CloneWithTerm(query, null);
                byId.ItemIds = candidates.Select(h => h.Id).ToArray();
                byId.StartIndex = null;
                byId.Limit = null;

                var found = (RunQuery(byId, dataContext, cancellationToken)?.Items ??
                             Array.Empty<BaseItem>()).ToList();

                // 人物不在任何媒体库目录下：不带类型的搜索（网页侧边栏搜索框就是这种）里，
                // Emby 是靠搜索词把人物额外带出来的；去掉搜索词按 id 取时会被媒体库范围过滤掉。
                // 所以人物候选单独按「人物」类型再取一次（只在没限定媒体库、且原查询允许人物时）。
                var gotIds = new System.Collections.Generic.HashSet<long>(found.Select(i => i.InternalId));
                var people = candidates.Where(h => h.Type == "Person" && !gotIds.Contains(h.Id))
                    .Select(h => h.Id).ToArray();
                if (people.Length > 0 && AllowsPeople(query))
                {
                    var personQuery = new InternalItemsQuery(query.User)
                    {
                        ItemIds = people,
                        IncludeItemTypes = new[] { "Person" },
                        Recursive = true,
                        DtoOptions = query.DtoOptions
                    };
                    var persons = RunQuery(personQuery, dataContext, cancellationToken)?.Items;
                    if (persons != null) found.AddRange(persons.Where(i => gotIds.Add(i.InternalId)));
                }

                var rank = candidates.ToDictionary(h => h.Id, h => h.Score);
                var ordered = FilterScope(found.ToArray(), scope)
                    .OrderByDescending(i => rank.TryGetValue(i.InternalId, out var sc) ? sc : 0)
                    .ToArray();
                if (ordered.Length == 0) return;

                if (Merge(ref __result, ordered, true, limit))
                {
                    Plugin.Instance?.Logger?.Debug("ChineseSearch - 「" + term + "」模糊/拼音补充 " + ordered.Length + " 个条目");
                }
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("ChineseSearch - 增强查询失败: " + e.Message);
            }
            finally
            {
                InPatch.Value = false;
            }
        }

        /// <summary>
        /// 把补充结果并进原结果。append=false 时直接替换（Lite 模式原结果为空的情况）。返回是否有变化。
        /// 有 Limit 时只填满这一页剩下的位置：原结果已经占满一页就不追加（原生还有下一页，追加会和下一页重复）。
        /// </summary>
        private static bool Merge(ref QueryResult<BaseItem> result, BaseItem[] extra, bool append, int? limit)
        {
            if (extra == null || extra.Length == 0) return false;

            var existing = append && result?.Items != null ? result.Items : Array.Empty<BaseItem>();
            var room = limit.HasValue ? limit.Value - existing.Length : int.MaxValue;
            if (room <= 0) return false;

            var seen = new System.Collections.Generic.HashSet<long>(existing.Select(i => i.InternalId));
            var add = extra.Where(i => seen.Add(i.InternalId)).Take(room).ToArray();
            if (add.Length == 0) return false;

            var merged = existing.Concat(add).ToArray();
            result = new QueryResult<BaseItem> { Items = merged, TotalRecordCount = merged.Length };
            return true;
        }

        /// <summary>原查询会不会返回人物：没限定媒体库 / 父条目，类型没限定或包含人物，且没排除人物。</summary>
        private static bool AllowsPeople(InternalItemsQuery query)
        {
            if (query.ParentIds != null && query.ParentIds.Length > 0) return false;
            if (query.ItemIds != null && query.ItemIds.Length > 0) return false;
            var include = query.IncludeItemTypes ?? Array.Empty<string>();
            if (include.Length > 0 && !include.Contains("Person", StringComparer.OrdinalIgnoreCase)) return false;
            var exclude = query.ExcludeItemTypes ?? Array.Empty<string>();
            return !exclude.Contains("Person", StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>「搜索范围」过滤；选项值和 Emby 类型名不完全一样（合集=BoxSet、音乐=Audio、直播=TvChannel）。</summary>
        private static BaseItem[] FilterScope(BaseItem[] items, string[] scope)
        {
            if (items == null || items.Length == 0) return Array.Empty<BaseItem>();
            if (scope == null || scope.Length == 0) return items;

            return items.Where(i => scope.Any(s => ScopeMatches(s, i.GetType().Name))).ToArray();
        }

        internal static bool ScopeMatches(string scope, string typeName)
        {
            if (string.Equals(scope, typeName, StringComparison.OrdinalIgnoreCase)) return true;
            switch (scope)
            {
                case "Collection": return typeName == "BoxSet";
                case "Music": return typeName == "Audio";
                case "LiveTv": return typeName == "TvChannel" || typeName == "LiveTvChannel" || typeName == "LiveTvProgram";
                default: return false;
            }
        }

        private static readonly PropertyInfo[] QueryProperties = typeof(InternalItemsQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray();

        /// <summary>
        /// 复制一份查询，只替换搜索词。
        /// 必须整份复制：以前只抄了十来个字段，媒体库范围、排除类型、虚拟条目、
        /// 家长控制等过滤条件都丢了，变体查询的结果会比原查询"宽"。
        /// </summary>
        internal static InternalItemsQuery CloneWithTerm(InternalItemsQuery source, string term)
        {
            var clone = new InternalItemsQuery(source.User);
            foreach (var p in QueryProperties)
            {
                try { p.SetValue(clone, p.GetValue(source)); }
                catch { /* 个别只读计算属性跳过 */ }
            }

            clone.SearchTerm = term;
            return clone;
        }
    }
}
