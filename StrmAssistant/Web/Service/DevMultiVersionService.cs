using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevMultiVersionService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public DevMultiVersionService(ILibraryManager libraryManager) => _libraryManager = libraryManager;

        public object Get(DevMultiVersion request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new { ok = false, error = "条目不存在" };

            var video = item as Video;
            var alternates = video?.GetAlternateVersionIds()?.ToList() ?? new System.Collections.Generic.List<long>();

            return new
            {
                ok = true,
                name = item.Name,
                itemId = item.InternalId,
                alternateCount = alternates.Count,
                alternateIds = alternates
            };
        }
    }
}
