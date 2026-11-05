using System;
using System.Threading.Tasks;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins.UI.Views;

namespace StrmAssistant.Options.UI
{
    /// <summary>
    /// 移植自 PRO 的页面视图基类（A_0093_…）。
    /// 行为一致：把一个 IEditableObject 渲染成插件页面，带保存/返回命令。
    /// </summary>
    public class PluginPageView : IPluginUIView, IPluginPageView, IPluginViewWithOptions
    {
        private IEditableObject _contentData;

        public PluginPageView(string pluginId, IEditableObject contentData, UserDto user = null)
        {
            PluginId = pluginId;
            _contentData = contentData;
            User = user;
        }

        public string Caption => (_contentData as EditableObjectBase)?.EditorTitle;

        public string SubCaption => (_contentData as EditableObjectBase)?.EditorDescription;

        public string PluginId { get; }

        public IEditableObject ContentData
        {
            get => _contentData;
            set
            {
                _contentData = value;
                UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));
            }
        }

        public UserDto User { get; set; }

        public string RedirectViewUrl { get; set; }

        public PluginViewOptions ViewOptions { get; } = new PluginViewOptions();

        public bool ShowSave { get; set; } = true;

        public bool ShowBack { get; set; }

        public bool AllowSave { get; set; } = true;

        public bool AllowBack { get; set; }

        public event EventHandler<GenericEventArgs<IPluginUIView>> UIViewInfoChanged;

        public bool IsCommandAllowed(string commandKey) => true;

        /// <summary>命令处理器：由具体页面注入（对应 PRO 各视图的 RunCommand 分支）。</summary>
        public Func<string, string, string, Task<IPluginUIView>> CommandHandler { get; set; }

        public Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            // 「配置」按钮：打开弹窗；「执行」按钮：立即运行计划任务，然后照常刷新页面
            var ours = Dialogs.OptionDialogs.PickCommand(commandId, data);
            if (Dialogs.OptionDialogs.IsDialog(ours))
            {
                var dialog = Dialogs.OptionDialogs.Open(PluginId, ours);
                if (dialog != null) return Task.FromResult(dialog);
            }
            else if (Dialogs.OptionDialogs.IsTask(ours))
            {
                DashboardOptions.LastActionResult = Dialogs.OptionDialogs.RunTask(ours);
            }

            if (CommandHandler != null) return CommandHandler(itemId, commandId, data);

            if (commandId == "PageSave" && SaveHandler != null) return SaveHandler(_contentData);

            // 默认：仪表盘维护命令
            if (commandId == "RefreshMemory" || commandId == "RebuildSearchIndex" ||
                commandId == "OptimizeSearchIndex")
            {
                DashboardOptions.RefreshIfExists();
                RaiseInfoChanged();
            }

            return Task.FromResult<IPluginUIView>(this);
        }

        private void RaiseInfoChanged() =>
            UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));

        /// <summary>保存处理器：由具体页面注入（对应 PRO 的保存分支）。
        /// Emby 会先把请求体反序列化进 ContentData，再调用 RunCommand("PageSave")。</summary>
        public Func<IEditableObject, Task<IPluginUIView>> SaveHandler { get; set; }

        public Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            if (SaveHandler != null) return SaveHandler(_contentData);
            return Task.FromResult<IPluginUIView>(this);
        }

        public Task Cancel() => Task.CompletedTask;

        /// <summary>重新取页面内容（弹窗保存后刷新页面上的摘要）。</summary>
        public Func<IEditableObject> Reload { get; set; }

        public void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
        {
            if (!completedOk || Reload == null) return;
            try { ContentData = Reload(); }
            catch (Exception e) { Plugin.Instance?.Logger?.Info("OptionDialog - 刷新页面失败: " + e.Message); }
        }
    }
}
