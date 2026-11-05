using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevExtractOneService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public DevExtractOneService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public async Task<object> Post(DevExtractOne request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new { ok = false, error = "条目不存在" };

            string subtitlePath = null;
            if (request.MakeGbKSubtitle && !string.IsNullOrWhiteSpace(item.Path))
            {
                var dir = Path.GetDirectoryName(item.Path);
                if (!string.IsNullOrEmpty(dir))
                {
                    subtitlePath = Path.Combine(dir, Path.GetFileNameWithoutExtension(item.Path) + ".zh.srt");
                    var content = "1\n00:00:01,000 --> 00:00:02,000\n联调中文字幕\n";
                    try { File.WriteAllBytes(subtitlePath, Encoding.GetEncoding("GB18030").GetBytes(content)); }
                    catch (System.Exception e) { _logger.Info("Dev/Extract - 写字幕失败: " + e.Message); }
                }
            }

            var result = await Plugin.LibraryApi
                .OrchestrateMediaInfoProcessAsync(item, "Dev/Extract", CancellationToken.None, true, true)
                .ConfigureAwait(false);

            bool? serialized = null;
            if (request.ForceSerialize)
            {
                serialized = await Plugin.MediaInfoApi
                    .SerializeMediaInfo(item.InternalId, null, true, "Dev/Extract")
                    .ConfigureAwait(false);
            }

            return new { ok = true, result, serialized, subtitlePath };
        }
    }
}
