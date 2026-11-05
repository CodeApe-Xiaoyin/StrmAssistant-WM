using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    /// <summary>
    /// 移植自 PRO「豆瓣辅助刮削」的写入步骤：
    /// 用豆瓣的评分/简介覆盖条目的社区评分与简介。
    /// </summary>
    public class DoubanApplyService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly DoubanApi _doubanApi;

        public DoubanApplyService(ILibraryManager libraryManager, IHttpClient httpClient)
        {
            _libraryManager = libraryManager;
            _doubanApi = new DoubanApi(httpClient);
        }

        public async Task<object> Post(ApplyDoubanToItem request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return null;

            var info = await _doubanApi
                .GetInfoAsync(request.DoubanId, request.Type, CancellationToken.None)
                .ConfigureAwait(false);

            if (info == null) return null;

            if (Common.DryRunMode.Skip("应用豆瓣信息 " + item.Name))
                return null;

            if (!string.IsNullOrWhiteSpace(info.Intro))
                item.Overview = info.Intro;

            var normalizedRating = Common.RatingGuard.Normalize(info.Rating?.Value);
            if (normalizedRating.HasValue)
                item.CommunityRating = normalizedRating.Value;

            _libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit);

            Plugin.Instance.Logger.Info("Douban info applied to {0}", item.Name);
            return null;
        }
    }
}
