using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using StrmAssistant.Common;
using StrmAssistant.Properties;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>清理本地元数据缓存中已无对应条目的目录。</summary>
    public class MetadataCacheCleanupTask : IScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Resources.Culture);

        public string Key => "MetadataCacheCleanupTask";

        public string Name => "清理元数据缓存";

        public string Description => "删除缓存目录中已经没有对应条目的残留文件";

        public bool IsHidden => false;

        public bool IsEnabled => true;

        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            var root = options?.MetadataCacheRootFolder;
            var libraryManager = Plugin.LibraryManager;

            if (string.IsNullOrWhiteSpace(root) || libraryManager == null)
            {
                _logger.Info("MetadataCacheCleanup - cache root not configured, skipped");
                return Task.CompletedTask;
            }

            var items = libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { typeof(Series).Name, typeof(Movie).Name }
            });

            var service = new MetadataCacheCleanupService(libraryManager, _logger);
            var removed = service.Cleanup(root, items);

            _logger.Info($"MetadataCacheCleanup - removed {removed} orphan folder(s)");
            Common.TaskReporter.Report("清理元数据缓存", "元数据缓存清理完成", _logger);
                        return Task.CompletedTask;
        }
    }
}
