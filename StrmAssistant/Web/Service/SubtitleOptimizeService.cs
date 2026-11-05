using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class SubtitleOptimizeService : IService
    {
        public object Post(SubtitleOptimizeApi request)
        {
            var path = request?.Path;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return new { ok = false, error = "文件不存在" };

            var changed = SubtitleOptimizer.Optimize(path, true, out var message);
            return new { ok = true, changed, message };
        }
    }
}
