using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;

namespace StrmAssistant.Options.UI
{
    /// <summary>
    /// 标签页容器（移植自 PRO 的 tab 容器控制器）：把多个 tab 控制器聚合成一页。
    /// </summary>
    public class TabbedPageController : ControllerBase, IHasTabbedUIPages
    {
        private readonly PluginPageInfo _pageInfo;
        private readonly IReadOnlyList<IPluginUIPageController> _tabs;
        private readonly Func<IEditableObject> _ownOptionsAccessor;
        private readonly Func<IPluginUIView, IEditableObject, Task<IPluginUIView>> _saveHandler;
        private readonly IPluginUIPageController _defaultPage;

        public TabbedPageController(string pluginId, PluginPageInfo pageInfo,
            IEnumerable<IPluginUIPageController> tabs, Func<IEditableObject> ownOptionsAccessor = null,
            Func<IPluginUIView, IEditableObject, Task<IPluginUIView>> saveHandler = null,
            IPluginUIPageController defaultPage = null) : base(pluginId)
        {
            _defaultPage = defaultPage;
            _pageInfo = pageInfo;
            _tabs = tabs.ToList();
            _ownOptionsAccessor = ownOptionsAccessor;
            _saveHandler = saveHandler;
        }

        public override PluginPageInfo PageInfo => _pageInfo;

        public IReadOnlyList<IPluginUIPageController> TabPageControllers => _tabs;

        public override Task<IPluginUIView> CreateDefaultPageView()
        {
            // 主页（第一个标签）有自己的页面时用它，不再把全部设置重复渲染一遍
            if (_defaultPage != null) return _defaultPage.CreateDefaultPageView();

            if (_ownOptionsAccessor != null)
            {
                var view = new PluginPageView(PluginId, _ownOptionsAccessor());
                if (_saveHandler != null) view.SaveHandler = content => _saveHandler(view, content);
                return Task.FromResult<IPluginUIView>(view);
            }
            return _tabs[0].CreateDefaultPageView();
        }
    }
}
