using MediaBrowser.Model.Services;
using StrmAssistant.Common.Hooks;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class HookSelfTestService : IService
    {
        public object Get(HookSelfTestApi request) => HookSelfTest.Run();
    }
}
