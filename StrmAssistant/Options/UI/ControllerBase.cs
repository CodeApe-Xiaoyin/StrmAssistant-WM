using System.Security;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Users;

namespace StrmAssistant.Options.UI
{
    /// <summary>
    /// 移植自 PRO 的 StrmAssistant.Options.UIBaseClasses.ControllerBase。
    /// 行为一致：页面控制器基类，仅管理员可访问。
    /// </summary>
    public abstract class ControllerBase : IPluginUIPageController, IPluginPageSecurity
    {
        internal static ControllerBase Current;

        public abstract PluginPageInfo PageInfo { get; }

        public string PluginId { get; }

        protected ControllerBase(string pluginId)
        {
            PluginId = pluginId;
            Current = this;
        }

        public virtual Task Initialize(CancellationToken token) => Task.CompletedTask;

        public abstract Task<IPluginUIView> CreateDefaultPageView();

        public Task CheckIsUserAuthorised(UserDto user, IPluginUIView requestedView)
        {
            var isAdmin = user?.Policy?.IsAdministrator ?? false;
            if (isAdmin) return Task.CompletedTask;
            throw new SecurityException("Access denied");
        }
    }
}
