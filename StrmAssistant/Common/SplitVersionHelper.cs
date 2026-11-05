using System;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 「拆分多版本」：把一个"多版本"条目重新拆成独立条目。
    /// Emby 自己提供 LibraryManager.SplitItems(BaseItem)，这里通过反射调用。
    /// </summary>
    public static class SplitVersionHelper
    {
        private static MethodInfo _splitItems;

        public static bool CanSplit(BaseItem item) => item is MediaBrowser.Controller.Entities.Video && GetSplitMethod() != null;

        private static MethodInfo GetSplitMethod()
        {
            if (_splitItems != null) return _splitItems;

            var type = Type.GetType("Emby.Server.Implementations.Library.LibraryManager, Emby.Server.Implementations");
            _splitItems = type?.GetMethod("SplitItems", BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(BaseItem) }, null);

            return _splitItems;
        }

        /// <summary>返回是否执行了拆分。</summary>
        public static bool TrySplit(BaseItem item, ILogger logger)
        {
            if (item == null) return false;

            var method = GetSplitMethod();
            if (method == null)
            {
                logger?.Info("SplitMovies - 找不到 LibraryManager.SplitItems");
                return false;
            }

            var video = item as MediaBrowser.Controller.Entities.Video;
            var alternates = video?.GetAlternateVersionIds();
            if (alternates == null || !alternates.Any())
            {
                logger?.Info("SplitMovies - 条目没有其它版本，跳过: " + item.Name);
                return false;
            }

            if (DryRunMode.Skip("拆分多版本 " + item.Name, logger)) return false;

            try
            {
                method.Invoke(Plugin.LibraryManager, new object[] { item });
                logger?.Info("SplitMovies - 已拆分: " + item.Name + "（原版本数 " + (alternates.Count() + 1) + "）");
                return true;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                logger?.Info("SplitMovies - 拆分失败: " + item.Name + " " + inner.Message);
                return false;
            }
        }
    }
}
