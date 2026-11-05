using System;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    /// <summary>移植自 PRO「深度删除」：条目删除后继续处理 strm / 软链接指向的目标。</summary>
    public class DeepDeleteService : IService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;

        public DeepDeleteService(ILibraryManager libraryManager, IFileSystem fileSystem, ILogger logger)
        {
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public object Post(DeepDeleteItem request)
        {
            if (!Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.EnableDeepDelete)
            {
                _logger.Info("Deep delete skipped: option disabled");
                return null;
            }

            if (Common.DryRunMode.Skip("深度删除 " + request.ItemId, _logger))
            {
                return null;
            }

            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return null;

            var path = item.Path;
            var strmTarget = ReadStrmTarget(path);

            _libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true });

            if (!string.IsNullOrEmpty(strmTarget) && File.Exists(strmTarget))
            {
                try
                {
                    _fileSystem.DeleteFile(strmTarget);
                    _logger.Info("Deep delete removed target: " + strmTarget);
                }
                catch (Exception e)
                {
                    _logger.Info("Deep delete target failed: " + e.Message);
                }
            }

            _logger.Info("Deep delete done for " + item.Name);
            return null;
        }

        private static string ReadStrmTarget(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".strm", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (!File.Exists(path)) return null;

                var content = File.ReadAllText(path).Trim();
                return content.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                    ? content.Substring(5).Trim()
                    : content;
            }
            catch
            {
                return null;
            }
        }
    }
}
