using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DoubanService : IService
    {
        private readonly DoubanApi _doubanApi;

        public DoubanService(IHttpClient httpClient)
        {
            _doubanApi = new DoubanApi(httpClient);
        }

        public Task<object> Get(GetDoubanInfo request) =>
            GetInfo(request.Id, request.Type);

        public async Task<object> Get(SearchDouban request)
        {
            try
            {
                return await _doubanApi.SearchAsync(request.Q, request.Type, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                Plugin.Instance.Logger.Info("Douban search failed: " + ex.Message);
                return null;
            }
        }

        private async Task<object> GetInfo(string id, string type)
        {
            try
            {
                return await _doubanApi.GetInfoAsync(id, type, CancellationToken.None).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                Plugin.Instance.Logger.Info("Douban info failed: " + ex.Message);
                return null;
            }
        }
    }
}
