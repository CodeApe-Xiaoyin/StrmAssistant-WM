using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using StrmAssistant.Common;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>
    /// 多版本标题修复。
    ///
    /// 「美化多版本显示」现在由 <see cref="Common.Hooks.MultiVersionDisplayPatch"/> 在版本下拉框里实时完成，
    /// 不再改条目标题。旧版本的这个任务会把标题改成「名称 - 清晰度」，并且每跑一次叠一层
    /// （例如「X - 未知 - 未知 - X (2024) (1)」）。这里把这种标题还原成原来的名字。
    /// 只处理有多版本关系的条目，只剥旧任务生成过的尾巴；没被改过的标题不动。
    /// </summary>
    public class MultiVersionBeautifyTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public MultiVersionBeautifyTask(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger("Strm Assistant");
        }

        public string Key => "MultiVersionBeautifyTask";
        public string Name => "多版本标题修复";
        public string Description => "还原旧版「多版本美化」改写过的条目标题（如「名称 - 4K」）；版本美化现在只作用于版本下拉框";
        public string Category =>
            StrmAssistant.Properties.Resources.ResourceManager.GetString(
                "PluginOptions_EditorTitle_Strm_Assistant", Plugin.Instance.DefaultUICulture);
        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("MultiVersionTitleRepair - Scheduled Task Execute");

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { nameof(Movie), nameof(Episode), nameof(Video) }
            }).ToList();

            var groups = new List<List<BaseItem>>();
            var visited = new HashSet<long>();

            foreach (var item in items)
            {
                if (visited.Contains(item.InternalId)) continue;

                var altIds = (item as Video)?.GetAlternateVersionIds() ?? new List<long>();
                if (altIds.Count == 0) continue;

                var group = new List<BaseItem> { item };
                visited.Add(item.InternalId);

                foreach (var altId in altIds)
                {
                    if (!visited.Add(altId)) continue;
                    var alt = _libraryManager.GetItemById(altId);
                    if (alt != null) group.Add(alt);
                }

                groups.Add(group);
            }

            _logger.Info("MultiVersionTitleRepair - 版本组: " + groups.Count);

            var repaired = 0;
            var done = 0;

            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileNames = group
                    .Select(i => System.IO.Path.GetFileNameWithoutExtension(i.Path ?? string.Empty))
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToList();

                foreach (var item in group)
                {
                    var restored = MultiVersionNaming.RestoreLegacyTitle(item.Name, fileNames);
                    if (restored == null) continue;

                    if (DryRunMode.Skip("多版本标题还原 " + item.Name + " → " + restored, _logger)) continue;

                    _logger.Info("MultiVersionTitleRepair - 「" + item.Name + "」→「" + restored + "」 " + item.Path);
                    item.Name = restored;
                    _libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit);
                    repaired++;
                }

                progress.Report(++done * 100.0 / groups.Count);
            }

            _logger.Info("MultiVersionTitleRepair - 完成，还原 " + repaired + " 个标题");
            TaskReporter.Report("多版本标题修复", "还原 " + repaired + " 个标题", _logger);
            progress.Report(100);
            return Task.CompletedTask;
        }
    }
}
