using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>拆分多版本电影：把合并过的多版本条目重新拆开（对应 PRO 的「拆分多版本」按钮）。</summary>
    public class SplitMoviesTask : IScheduledTask
    {
        private static readonly Guid TaskId = new Guid("8f3c1a52-4b6d-4e21-9a77-2c5d0e9b1a34");

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public SplitMoviesTask(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
            _logger = Plugin.Instance.Logger;
        }

        public string Key => "SplitMoviesTask";
        public string Name => "拆分多版本电影";
        public string Description => "把合并过的多版本电影重新拆成独立条目";
        public string Category =>
            StrmAssistant.Properties.Resources.ResourceManager.GetString(
                "PluginOptions_EditorTitle_Strm_Assistant", Plugin.Instance.DefaultUICulture);
        public bool IsHidden => false;
        public bool IsEnabled => true;
        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("SplitMovies - Scheduled Task Execute");

            var movies = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Movie" },
                Recursive = true
            });

            var targets = movies
                .OfType<MediaBrowser.Controller.Entities.Video>()
                .Where(v => (v.GetAlternateVersionIds() ?? new List<long>()).Any())
                .Cast<BaseItem>()
                .ToList();
            _logger.Info("SplitMovies - 候选多版本条目: " + targets.Count);

            var done = 0;
            var total = Math.Max(1, targets.Count);
            var index = 0;

            foreach (var item in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Common.SplitVersionHelper.TrySplit(item, _logger)) done++;
                progress.Report(++index * 100.0 / total);
            }

            _logger.Info("SplitMovies - 完成，拆分 " + done + "/" + targets.Count);
            Common.TaskReporter.Report("拆分多版本电影", "多版本拆分完成", _logger);
                        return Task.CompletedTask;
        }
    }
}
