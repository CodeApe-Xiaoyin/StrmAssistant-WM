using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.EntryPoints
{
    /// <summary>启动时清理 TMDB 缓存（对应 PRO 的 ClearRecentTmdbCacheOnStartup）。</summary>
    public class TmdbCacheCleanupEntryPoint : IServerEntryPoint
    {
        private readonly ILogger _logger;

        public TmdbCacheCleanupEntryPoint(ILogger logger)
        {
            _logger = logger;
        }

        public void Run()
        {
            var options = Plugin.Instance.GetPluginOptions().MetadataEnhanceOptions;
            if (!options.ClearRecentTmdbCacheOnStartup) return;

            var removed = Common.TmdbCache.Clear();
            _logger.Info("TmdbCacheCleanup - removed " + removed + " cached responses");
        }

        public void Dispose()
        {
        }
    }
}
