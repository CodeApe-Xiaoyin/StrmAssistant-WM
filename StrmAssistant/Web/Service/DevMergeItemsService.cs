using System;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevMergeItemsService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public DevMergeItemsService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public void Post(DevMergeItems request)
        {
            // 与其它 Dev 端点保持一致：只要求 Admin 鉴权（见 DevMergeItems 的 [Authenticated]）。
            // 之前这里额外要求 DebugMode，但 DebugMode 是 [Browsable(false)]，
            // 页面上改不了、每次保存选项还会被覆盖回 false，导致自动化探针时好时坏。

            var ids = (request.Ids ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();

            if (ids.Length < 2)
            {
                _logger.Info("Dev/MergeItems - 至少需要两个 id");
                return;
            }

            var items = ids.Select(id => _libraryManager.GetItemById(id))
                .Where(i => i != null)
                .ToArray();

            if (items.Length < 2)
            {
                _logger.Info("Dev/MergeItems - 只找到 " + items.Length + " 个条目");
                return;
            }

            _libraryManager.MergeItems(items);

            _logger.Info("Dev/MergeItems - 已合并 " + items.Length + " 个条目: " +
                         string.Join(" + ", items.Select(i => i.Name)));

            // 合并后打印版本关系（BaseItem 上无扩展方法，改用反射读属性）
            foreach (var it in items)
            {
                var altProp = it.GetType().GetMethod("GetAlternateVersionIds");
                var altCount = "n/a";
                if (altProp != null)
                {
                    var val = altProp.Invoke(it, null) as System.Collections.ICollection;
                    altCount = (val?.Count ?? 0).ToString();
                }

                var primary = it.GetType().GetProperty("PrimaryVersionId")?.GetValue(it) as string;

                _logger.Info("Dev/MergeItems - 条目 " + it.Name + " (" + it.InternalId + ")" +
                             " 主版本=" + (primary ?? "null") +
                             " 备用版本数=" + altCount);
            }
        }
    }
}
