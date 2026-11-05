using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace StrmAssistant.Web.Api
{
    [Unauthenticated]
    [Route("/StrmAssistant/Script", "GET", Summary = "Injected front-end script")]
    public class ScriptInjectApi : IRequiresRequest
    {
        public IRequest Request { get; set; }
    }
}
