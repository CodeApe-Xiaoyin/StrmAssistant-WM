using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class ChapterReadService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;

        public ChapterReadService(ILibraryManager libraryManager, IItemRepository itemRepository)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
        }

        public object Get(GetEpisodeChapters request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new List<object>();

            return _itemRepository.GetChapters(item, CancellationToken.None);
        }
    }
}
