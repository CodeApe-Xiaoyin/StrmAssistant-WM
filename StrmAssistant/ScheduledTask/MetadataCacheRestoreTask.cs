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
    /// <summary>从本地元数据缓存还原条目信息。</summary>
    public class MetadataCacheRestoreTask : IScheduledTask
    {
        private readonly ILogger _logger = Plugin.Instance.Logger;

        public string Category => Resources.ResourceManager.GetString("PluginOptions_EditorTitle_Strm_Assistant",
            Resources.Culture);

        public string Key => "MetadataCacheRestoreTask";

        public string Name => "从缓存还原元数据";

        public string Description => "读取本地元数据缓存并回写条目（缓存优先）";

        public bool IsHidden => false;

        public bool IsEnabled => true;

        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            _logger.Info("MetadataCacheRestore - Scheduled Task Execute");
            _logger.Info("Cache root: " + options.MetadataCacheRootFolder);

            var root = options.MetadataCacheRootFolder;
            var libraryManager = Plugin.LibraryManager;

            if (string.IsNullOrWhiteSpace(root) || libraryManager == null)
            {
                _logger.Info("MetadataCacheRestore - cache root not configured, skipped");
                return Task.CompletedTask;
            }

            var items = libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { typeof(Series).Name, typeof(Movie).Name }
            });

            var service = new MetadataCacheRestoreService(libraryManager, _logger);
            var restored = 0;

            foreach (var entity in items)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var item = libraryManager.GetItemById(entity.Id) ?? entity;
                    if (service.Restore(item, root)) restored++;
                }
                catch (Exception ex)
                {
                    _logger.Info("MetadataCacheRestore - item failed for " + entity.Name + ": " + ex.Message);
                }
            }

            _logger.Info("MetadataCacheRestore - restored: " + restored);
            Common.TaskReporter.Report("恢复元数据缓存", "元数据缓存恢复完成", _logger);
                        return Task.CompletedTask;
        }
    }
}
