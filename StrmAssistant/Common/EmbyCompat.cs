#if EMBY48
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;

namespace StrmAssistant
{
    /// <summary>
    /// Emby 4.8 兼容层：补上 4.9+ 才有的带 CancellationToken 的重载，
    /// 让业务代码在各版本下写法一致。只在 EmbyTarget=4.8 时参与编译。
    /// （放在 StrmAssistant 根命名空间，所有子命名空间都能直接用到这些扩展方法。）
    /// </summary>
    internal static class EmbyCompat
    {
        public static List<ChapterInfo> GetChapters(this IItemRepository repository, long itemId,
            MarkerType[] markerTypes, CancellationToken cancellationToken)
            => repository.GetChapters(itemId, markerTypes);

        public static List<ChapterInfo> GetChapters(this IItemRepository repository, BaseItem item,
            CancellationToken cancellationToken)
            => repository.GetChapters(item);
    }
}
#endif
