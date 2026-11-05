using System.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevMediaInfoJsonService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public DevMediaInfoJsonService(ILibraryManager libraryManager) => _libraryManager = libraryManager;

        public object Delete(DevMediaInfoJson request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new { ok = false, error = "条目不存在" };

            var path = MediaInfoApi.GetMediaInfoJsonPath(item);
            var existed = File.Exists(path);

            try { if (existed) File.Delete(path); }
            catch (System.Exception e) { return new { ok = false, error = e.Message, path }; }

            return new { ok = true, deleted = existed, path };
        }

        public object Get(DevMediaInfoJson request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new { ok = false, error = "条目不存在" };

            var path = MediaInfoApi.GetMediaInfoJsonPath(item);
            var mode = Plugin.Instance?.GetPluginOptions()?.MediaInfoExtractOptions?.PersistMediaInfoMode;

            return new { ok = true, mode, path, exists = File.Exists(path) };
        }
    }
}
