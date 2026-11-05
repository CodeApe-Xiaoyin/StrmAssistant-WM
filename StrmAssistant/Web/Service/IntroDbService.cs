using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class IntroDbService : IService
    {
        private readonly IntroDbApi _introDbApi;

        public IntroDbService(IHttpClient httpClient)
        {
            _introDbApi = new IntroDbApi(httpClient);
        }

        public async Task<object> Get(GetIntroDbMarker request) =>
            await _introDbApi.GetMarkerAsync(request.ImdbId, request.Season, request.Episode, CancellationToken.None)
                .ConfigureAwait(false);
    }
}
