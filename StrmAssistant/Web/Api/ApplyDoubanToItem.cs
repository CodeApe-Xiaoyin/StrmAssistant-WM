using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>把豆瓣数据写入指定条目（手动刮削，对应 PRO 豆瓣辅助刮削的落库部分）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Douban/ApplyToItem", "POST", Summary = "Apply Douban info to an item")]
    public class ApplyDoubanToItem : IReturnVoid
    {
        [ApiMember(Name = "ItemId", Description = "Emby item id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }

        [ApiMember(Name = "DoubanId", Description = "Douban id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string DoubanId { get; set; }

        [ApiMember(Name = "Type", Description = "movie|tv", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string Type { get; set; }
    }
}
