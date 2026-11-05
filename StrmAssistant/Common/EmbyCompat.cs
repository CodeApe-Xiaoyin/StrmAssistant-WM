using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace StrmAssistant
{
    /// <summary>
    /// Emby 4.8 / 4.9 / 4.10 共用一个插件包的兼容层。
    ///
    /// 插件按最老的 4.8 接口（.NET 6）编译：新版 Emby 能加载引用旧接口的插件，反过来不行。
    /// 三个版本签名不一样、或者只有新版才有的成员，都在这里运行时按实际情况调用（反射，结果缓存）：
    ///   · IItemRepository.GetChapters：4.9+ 多了 CancellationToken 参数
    ///   · IMediaSourceManager.GetStaticMediaSources：4.8 是 6 个参数，4.9+ 是 8 个
    ///   · BaseItem.IdString：4.10 才有，旧版用 Id.ToString("N")
    ///   · MetadataRefreshOptions.Recursive、通知 / 活动日志的 Severity：4.8 没有，有就设
    /// （放在 StrmAssistant 根命名空间，所有子命名空间都能直接用到这些扩展方法。）
    /// </summary>
    internal static class EmbyCompat
    {
        private static readonly ConcurrentDictionary<string, MethodInfo> Methods = new ConcurrentDictionary<string, MethodInfo>();

        private static MethodInfo Find(Type type, string name, Func<ParameterInfo[], bool> match)
        {
            var key = type.FullName + "::" + name + "::" + match.Method.Name + match.Method.MetadataToken;
            return Methods.GetOrAdd(key, _ =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Concat(type.GetInterfaces().SelectMany(i => i.GetMethods()))
                    .FirstOrDefault(m => m.Name == name && match(m.GetParameters())));
        }

        private static bool Is<T>(ParameterInfo p) => p.ParameterType == typeof(T);

        // ---------------- 章节 ----------------

        /// <summary>按条目 id 和标记类型取章节（4.9+ 带 CancellationToken 的重载优先）。</summary>
        public static List<ChapterInfo> GetChapters(this IItemRepository repository, long itemId,
            MarkerType[] markerTypes, CancellationToken cancellationToken)
        {
            var withToken = Find(repository.GetType(), "GetChapters",
                p => p.Length == 3 && Is<long>(p[0]) && Is<MarkerType[]>(p[1]) && Is<CancellationToken>(p[2]));
            if (withToken != null)
                return (List<ChapterInfo>)withToken.Invoke(repository, new object[] { itemId, markerTypes, cancellationToken });

            var plain = Find(repository.GetType(), "GetChapters",
                p => p.Length == 2 && Is<long>(p[0]) && Is<MarkerType[]>(p[1]));
            return (List<ChapterInfo>)plain.Invoke(repository, new object[] { itemId, markerTypes });
        }

        /// <summary>取条目的全部章节（4.9+ 带 CancellationToken 的重载优先）。</summary>
        public static List<ChapterInfo> GetChapters(this IItemRepository repository, BaseItem item,
            CancellationToken cancellationToken)
        {
            var withToken = Find(repository.GetType(), "GetChapters",
                p => p.Length == 2 && Is<BaseItem>(p[0]) && Is<CancellationToken>(p[1]));
            if (withToken != null)
                return (List<ChapterInfo>)withToken.Invoke(repository, new object[] { item, cancellationToken });

            var plain = Find(repository.GetType(), "GetChapters", p => p.Length == 1 && Is<BaseItem>(p[0]));
            return (List<ChapterInfo>)plain.Invoke(repository, new object[] { item });
        }

        // ---------------- 媒体源 ----------------

        /// <summary>
        /// GetStaticMediaSources：4.8 为 (item, enableAlternate, enablePathSubstitution, libraryOptions, deviceProfile, user)，
        /// 4.9+ 为 (item, enableAlternate, enablePathSubstitution, fillChapters?, ?, libraryOptions, deviceProfile, user)。
        /// 新加的两个参数传 false / null，与以前分版本编译时一致。
        /// </summary>
        public static List<MediaSourceInfo> GetStaticMediaSourcesCompat(object mediaSourceManager, BaseItem item,
            bool enableAlternateMediaSources, MediaBrowser.Model.Configuration.LibraryOptions libraryOptions)
        {
            var type = mediaSourceManager.GetType();
            var eight = Find(type, "GetStaticMediaSources", p => p.Length == 8 && Is<BaseItem>(p[0]));
            if (eight != null)
                return ToList(eight.Invoke(mediaSourceManager,
                    new object[] { item, enableAlternateMediaSources, false, false, null, libraryOptions, null, null }));

            var six = Find(type, "GetStaticMediaSources", p => p.Length == 6 && Is<BaseItem>(p[0]));
            if (six != null)
                return ToList(six.Invoke(mediaSourceManager,
                    new object[] { item, enableAlternateMediaSources, false, libraryOptions, null, null }));

            throw new MissingMethodException("IMediaSourceManager.GetStaticMediaSources（6 或 8 个参数）");
        }

        private static List<MediaSourceInfo> ToList(object result) =>
            result as List<MediaSourceInfo> ?? ((IEnumerable<MediaSourceInfo>)result)?.ToList() ?? new List<MediaSourceInfo>();

        // ---------------- 只有新版才有的成员 ----------------

        /// <summary>条目 id 的 32 位字符串（4.10 的 IdString，旧版拼 key 用的也是 Id.ToString("N")）。</summary>
        public static string IdStringCompat(this BaseItem item)
        {
            var prop = typeof(BaseItem).GetProperty("IdString", BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(item) as string ?? item.Id.ToString("N");
        }

        /// <summary>对象有这个可写属性就设上（4.8 没有的属性直接跳过）。枚举值按名字转换。</summary>
        public static void SetIfExists(object target, string property, object value)
        {
            var prop = target?.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite) return;
            if (value is string name && prop.PropertyType.IsEnum)
            {
                if (!Enum.TryParse(prop.PropertyType, name, true, out var parsed)) return;
                value = parsed;
            }

            prop.SetValue(target, value);
        }
    }
}
