using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.EntryPoints
{
    /// <summary>
    /// 字幕优化（对应 ExperienceEnhanceOptions.OptimizeSubtitle）。
    /// 订阅官方的 SubtitlesDownloaded 事件，字幕下载完成后统一转 UTF-8、可选转简体。
    /// </summary>
    public class SubtitleOptimizeEntryPoint : IServerEntryPoint
    {
        private readonly ISubtitleManager _subtitleManager;
        private readonly ILogger _logger;

        public SubtitleOptimizeEntryPoint(ISubtitleManager subtitleManager, ILogger logger)
        {
            _subtitleManager = subtitleManager;
            _logger = logger;
        }

        public void Run()
        {
            if (_subtitleManager == null) return;
            _subtitleManager.SubtitlesDownloaded += OnSubtitlesDownloaded;
            _logger.Info("SubtitleOptimize - subscribed to SubtitlesDownloaded");
        }

        private void OnSubtitlesDownloaded(object sender, SubtitleDownloadEventArgs e)
        {
            try
            {
                var options = Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions;
                if (options?.OptimizeSubtitle != true) return;

                var item = e?.Item;
                if (item == null) return;

                Task.Run(() => ProcessItem(item, _logger));
            }
            catch (Exception ex)
            {
                _logger.Info("SubtitleOptimize - failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 真正被"下载完成"事件调用的处理逻辑（含开关判断）。
        /// 调试端点也走这里，保证测的是同一条路径。
        /// </summary>
        public static int ProcessItem(MediaBrowser.Controller.Entities.BaseItem item,
            MediaBrowser.Model.Logging.ILogger logger)
        {
            if (item == null) return 0;

            var options = Plugin.Instance?.GetPluginOptions()?.ExperienceEnhanceOptions;
            if (options?.OptimizeSubtitle != true)
            {
                logger?.Info("SubtitleOptimize - 开关关闭，跳过: " + item.Name);
                return 0;
            }

            var changed = 0;
            foreach (var path in Common.SubtitleOptimizer.FindNearbySubtitles(item.Path))
            {
                if (Common.SubtitleOptimizer.Optimize(path, true, out var message))
                {
                    changed++;
                    logger?.Info($"SubtitleOptimize - {path} changed=True {message}");
                }
            }

            logger?.Info("SubtitleOptimize - 条目 " + item.Name + " 改动 " + changed + " 个字幕");
            return changed;
        }

        /// <summary>从条目路径推断所在目录，找出同目录下的字幕文件。</summary>
        private static System.Collections.Generic.IEnumerable<string> FindSubtitleFiles(
            MediaBrowser.Controller.Entities.BaseItem item)
        {
            var result = new System.Collections.Generic.List<string>();

            try
            {
                if (string.IsNullOrWhiteSpace(item.Path)) return result;

                var dir = System.IO.Directory.Exists(item.Path)
                    ? item.Path
                    : System.IO.Path.GetDirectoryName(item.Path);

                if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir)) return result;

                foreach (var file in System.IO.Directory.GetFiles(dir))
                {
                    if (Common.SubtitleOptimizer.IsSupported(file)) result.Add(file);
                }
            }
            catch
            {
                // 目录不可读时忽略
            }

            return result;
        }

        public void Dispose()
        {
            try { if (_subtitleManager != null) _subtitleManager.SubtitlesDownloaded -= OnSubtitlesDownloaded; } catch { }
        }
    }
}
