using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevAddCollectionService : IService
    {
        private readonly ICollectionManager _collectionManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public DevAddCollectionService(ICollectionManager collectionManager, ILibraryManager libraryManager, ILogger logger)
        {
            _collectionManager = collectionManager;
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public void Post(DevAddCollection request)
        {

            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null)
            {
                _logger.Info("Dev/AddCollection - 条目不存在: " + request.ItemId);
                return;
            }

            // ICollectionManager 接口上没有 AddCollection（那是实现类的内部方法），用反射调
            var method = _collectionManager.GetType().GetMethod("AddCollection",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic, null,
                new[] { typeof(MediaBrowser.Controller.Entities.BaseItem), typeof(string) }, null);

            if (method == null)
            {
                _logger.Info("Dev/AddCollection - 实现类型上找不到 AddCollection: " + _collectionManager.GetType().FullName);
                return;
            }

            var boxSet = method.Invoke(_collectionManager, new object[] { item, request.Name }) as MediaBrowser.Controller.Entities.BoxSet;
            _logger.Info("Dev/AddCollection - 调用完成，返回: " + (boxSet?.Name ?? "null") +
                         "（实现类型 " + _collectionManager.GetType().Name + "）");
        }
    }
}
