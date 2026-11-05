using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 移植自 PRO 的豆瓣评分更新：按首播日期回溯 N 天内的条目刷新评分。
    /// </summary>
    public class DoubanRatingService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public DoubanRatingService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public static bool WithinLookback(BaseItem item, int lookbackDays)
        {
            if (lookbackDays <= 0) return true;
            if (item.PremiereDate == null) return true;

            return item.PremiereDate.Value >= DateTime.UtcNow.AddDays(-lookbackDays);
        }

        public async Task<bool> ApplyAsync(BaseItem item, string doubanId, string type, CancellationToken token)
        {
            if (string.IsNullOrEmpty(doubanId)) return false;

            var api = new DoubanApi(Plugin.HttpClient);
            var info = await api.GetInfoAsync(doubanId, type, token).ConfigureAwait(false);
            if (info?.Rating == null) return false;

            // 越界评分（0、负数、>10）直接丢弃，避免把 55 这种百分制数字写进 CommunityRating
            var normalized = RatingGuard.Normalize(info.Rating.Value);
            if (!normalized.HasValue)
            {
                _logger.Info($"Douban rating - out of range skipped for {item.Name}: {info.Rating.Value}");
                return false;
            }

            if (item.CommunityRating.HasValue &&
                Math.Abs(item.CommunityRating.Value - normalized.Value) < 0.05f)
            {
                return false;
            }

            if (DryRunMode.Skip("写入豆瓣评分 " + item.Name + " = " + normalized.Value, _logger)) return false;

            item.CommunityRating = normalized.Value;
            _libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit);
            _logger.Info($"Douban rating updated for {item.Name}: {info.Rating.Value}");
            return true;
        }
    }
}
