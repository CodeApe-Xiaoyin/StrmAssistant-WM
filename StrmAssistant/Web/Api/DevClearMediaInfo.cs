using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Services;
using StrmAssistant.Common;

namespace StrmAssistant.Web.Api
{
    /// <summary>
    /// Dev only：清空某个条目的媒体流，用于验证「还原媒体信息」链路。
    /// 只清数据库里的流，不动文件；之后可用 Dev/ExtractMediaInfo 重新探测。
    /// </summary>
    [Route("/StrmAssistant/Dev/ClearMediaInfo", "POST", Summary = "Dev only: clear media streams of one item")]
    [MediaBrowser.Controller.Net.Authenticated(Roles = "Admin")]
    public class DevClearMediaInfo : IReturn<object>
    {
        public long ItemId { get; set; }
    }

    public class DevClearMediaInfoService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public DevClearMediaInfoService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public object Post(DevClearMediaInfo request)
        {
            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null) return new { ok = false, error = "item not found" };

            var before = item.MediaStreams?.Count ?? 0;

            // 顺带把 IItemRepository 里与 MediaStream 相关的方法列出来，便于确认有没有"删除"入口
            var repoType = Plugin.ItemRepository?.GetType();
            var methods = new List<string>();
            if (repoType != null)
            {
                foreach (var m in repoType.GetMethods())
                    if (m.Name.IndexOf("MediaStream", StringComparison.OrdinalIgnoreCase) >= 0)
                        methods.Add(m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), x => x.ParameterType.Name)) + ")");
            }

            try
            {
                Plugin.MediaInfoApi.PersistStreams(request.ItemId, new List<MediaBrowser.Model.Entities.MediaStream>());
                var after = _libraryManager.GetItemById(request.ItemId)?.MediaStreams?.Count ?? 0;
                return new { ok = true, before, after, methods };
            }
            catch (Exception e)
            {
                return new { ok = false, error = e.Message, before };
            }
        }
    }
}
