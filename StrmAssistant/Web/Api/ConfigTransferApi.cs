using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    /// <summary>导出插件配置（便于迁移到另一台机器）。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Config/Export", "GET", Summary = "Export plugin options")]
    public class ConfigExportApi : IReturn<object>
    {
    }

    /// <summary>导入插件配置。请求体为导出得到的 JSON。</summary>
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/Config/Import", "POST", Summary = "Import plugin options")]
    public class ConfigImportApi : IReturn<object>, IRequiresRequestStream
    {
        public System.IO.Stream RequestStream { get; set; }
    }
}
