using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevMdbListService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public DevMdbListService(ILibraryManager libraryManager) => _libraryManager = libraryManager;

        public async Task<object> Post(DevMdbList request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new { ok = false, error = "条目不存在" };

            var imdb = item.ProviderIds != null && item.ProviderIds.TryGetValue("Imdb", out var i0) ? i0 : null;
            var tmdb = item.ProviderIds != null && item.ProviderIds.TryGetValue("Tmdb", out var t0) ? t0 : null;
            var type = item.GetType().Name;

            var result = await MdbListApi
                .GetRatingAsync(imdb, tmdb, type, CancellationToken.None)
                .ConfigureAwait(false);

            return new
            {
                ok = result.Ok,
                score = result.Score,
                votes = result.Votes,
                status = result.StatusCode,
                error = result.Error,
                imdb,
                tmdb
            };
        }
    }
}
