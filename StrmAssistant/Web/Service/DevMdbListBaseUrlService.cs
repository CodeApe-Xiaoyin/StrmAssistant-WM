using MediaBrowser.Model.Services;
using StrmAssistant.Common;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DevMdbListBaseUrlService : IService
    {
        public object Post(DevMdbListBaseUrl request)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
                MdbListApi.BaseUrl = "https://api.mdblist.com";
            else
                MdbListApi.BaseUrl = request.Url.Trim();

            Plugin.Instance?.Logger?.Info("MdbList - 基地址改为 " + MdbListApi.BaseUrl);
            return new { ok = true, baseUrl = MdbListApi.BaseUrl };
        }
    }
}
