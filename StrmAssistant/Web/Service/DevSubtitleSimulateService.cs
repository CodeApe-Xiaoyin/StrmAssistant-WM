using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevSubtitleSimulateService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public DevSubtitleSimulateService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public object Post(DevSubtitleSimulate request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new { ok = false, error = "条目不存在" };

            var files = SubtitleOptimizer.FindNearbySubtitles(item.Path);

            // 走"下载完成"事件用的同一段逻辑（内含开关判断）
            var changed = EntryPoints.SubtitleOptimizeEntryPoint.ProcessItem(item, _logger);

            _logger.Info("Dev/SubtitleSimulate - 条目 " + item.Name + " 找到 " + files.Count + " 个字幕，改动 " + changed + " 个");

            return new { ok = true, found = files.Count, changed, files = files.Select(f => f).ToArray() };
        }
    }
}
