using System;
using System.Threading.Tasks;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;

namespace StrmAssistant.Options.UI
{
    /// <summary>
    /// 单个 tab 的页面控制器（移植自 PRO 的分页控制器行为）：
    /// 一个 tab = 一个选项对象 + 一个页面视图。
    /// </summary>
    public class TabPageController : ControllerBase
    {
        private readonly PluginPageInfo _pageInfo;
        private readonly bool _showSave;
        private readonly Func<IEditableObject> _optionsAccessor;
        private readonly Func<IPluginUIView, string, string, string, System.Threading.Tasks.Task<IPluginUIView>> _commandHandler;
        private readonly Func<IPluginUIView, MediaBrowser.Model.GenericEdit.IEditableObject, System.Threading.Tasks.Task<IPluginUIView>> _saveHandler;

        public TabPageController(string pluginId, string name, string displayName, string menuIcon,
            Func<IEditableObject> optionsAccessor,
            Func<IPluginUIView, string, string, string, System.Threading.Tasks.Task<IPluginUIView>> commandHandler = null,
            Func<IPluginUIView, MediaBrowser.Model.GenericEdit.IEditableObject, System.Threading.Tasks.Task<IPluginUIView>> saveHandler = null,
            bool showSave = true) : base(pluginId)
        {
            _showSave = showSave;
            _optionsAccessor = optionsAccessor;
            _commandHandler = commandHandler;
            _saveHandler = saveHandler;
            _pageInfo = new PluginPageInfo
            {
                Name = name,
                DisplayName = displayName,
                MenuIcon = menuIcon,
                EnableInMainMenu = false,
                EnableInUserMenu = false
            };
        }

        public override PluginPageInfo PageInfo => _pageInfo;

        public override Task<IPluginUIView> CreateDefaultPageView()
        {
            var view = new PluginPageView(PluginId, _optionsAccessor()) { Reload = _optionsAccessor, ShowSave = _showSave, AllowSave = _showSave };
            if (_saveHandler != null)
                view.SaveHandler = content => _saveHandler(view, content);
            if (_commandHandler != null)
                view.CommandHandler = (itemId, commandId, data) => _commandHandler(view, itemId, commandId, data);
            return Task.FromResult<IPluginUIView>(view);
        }
    }
}
