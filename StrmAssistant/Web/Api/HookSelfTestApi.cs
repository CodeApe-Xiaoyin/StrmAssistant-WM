using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    [Authenticated(Roles = "Admin")]
    [Route("/StrmAssistant/HookSelfTest", "GET", Summary = "Runtime hook self test")]
    public class HookSelfTestApi : IReturn<string>
    {
    }
}
