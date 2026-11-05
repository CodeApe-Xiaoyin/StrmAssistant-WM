using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>深度删除：删条目的同时删掉 .strm / 软链接指向的实体文件（对应 PRO「深度删除」）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Item/DeepDelete", "POST", Summary = "Delete item and its underlying strm/symlink target")]
    public class DeepDeleteItem : IReturnVoid
    {
        [ApiMember(Name = "ItemId", Description = "Emby item id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }
    }
}
