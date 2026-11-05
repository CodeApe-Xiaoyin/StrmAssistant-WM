using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevCaptureImageService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly ImageCaptureService _capture;

        public DevCaptureImageService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _capture = new ImageCaptureService(libraryManager, logger);
        }

        public async Task<object> Post(DevCaptureImage request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new { ok = false, error = "条目不存在" };

            var result = await _capture.CaptureAsync(item, request.Position, CancellationToken.None)
                .ConfigureAwait(false);

            _logger.Info("Dev/CaptureImage - " + item.Name + " 结果=" + result);
            return new { ok = true, captured = result, item = item.Name };
        }
    }
}
