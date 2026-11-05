using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>把外部片头库的标记写成章节（片头开始/结束/片尾开始）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/IntroDb/Apply", "POST", Summary = "Write external intro marker into episode chapters")]
    public class ApplyIntroDbMarker : IReturnVoid
    {
        [ApiMember(Name = "ItemId", Description = "Episode item id", IsRequired = true, DataType = "string", ParameterType = "query")]
        public string ItemId { get; set; }

        [ApiMember(Name = "ImdbId", Description = "IMDb id (defaults to series provider id)", IsRequired = false, DataType = "string", ParameterType = "query")]
        public string ImdbId { get; set; }
    }
}
