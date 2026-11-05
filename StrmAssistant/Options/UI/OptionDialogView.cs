using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Events;
using MediaBrowser.Model.GenericEdit;
using MediaBrowser.Model.Plugins.UI.Views;
using MediaBrowser.Model.Plugins.UI.Views.Enums;

namespace StrmAssistant.Options.UI
{
    /// <summary>
    /// 弹窗视图：页面上「配置」按钮打开的小窗口（做法同 MediaInfoKeeper）。
    /// 点「保存」时 Emby 把弹窗里的值以 JSON 传进 <see cref="OnOkCommand"/>，这里还原成对象后交给 onOk 写回设置。
    /// </summary>
    public class OptionDialogView : IPluginUIView, IPluginDialogView, IPluginViewWithOptions
    {
        private readonly string _caption;
        private readonly Action<EditableOptionsBase> _onOk;

        public OptionDialogView(string pluginId, EditableOptionsBase content, string caption, DialogSize size,
            Action<EditableOptionsBase> onOk)
        {
            PluginId = pluginId;
            ContentData = content;
            _caption = caption ?? string.Empty;
            _onOk = onOk;
            ViewOptions = new PluginViewOptions { DialogSize = size, OKButtonCaption = "保存" };
        }

        public string Caption => _caption;
        public string SubCaption => string.Empty;
        public string PluginId { get; }
        public IEditableObject ContentData { get; set; }
        public UserDto User { get; set; }
        public string RedirectViewUrl { get; set; }
        public PluginViewOptions ViewOptions { get; }

        public bool AllowCancel { get; set; } = true;
        public bool AllowOk { get; set; } = true;
        public bool ShowDialogFullScreen => false;

        public event EventHandler<GenericEventArgs<IPluginUIView>> UIViewInfoChanged;

        public bool IsCommandAllowed(string commandKey) => true;

        // 必须返回 null：Emby 先调这里，返回非空就停在返回的视图上，「保存 / 取消」不会再往下走
        public Task<IPluginUIView> RunCommand(string itemId, string commandId, string data) =>
            Task.FromResult<IPluginUIView>(null);

        public Task Cancel() => Task.CompletedTask;

        public void OnDialogResult(IPluginUIView dialogView, bool completedOk, object data)
        {
        }

        public Task OnCancelCommand() => Task.CompletedTask;

        public Task OnOkCommand(string providerId, string commandId, string data)
        {
            var content = ContentData as EditableOptionsBase;
            if (content == null) return Task.CompletedTask;

            var posted = Parse(content.GetType(), data);
            if (posted != null) OptionCopy.CopyEditable(posted, content);

            _onOk?.Invoke(content);
            UIViewInfoChanged?.Invoke(this, new GenericEventArgs<IPluginUIView>(this));
            return Task.CompletedTask;
        }

        private static EditableOptionsBase Parse(Type type, string data)
        {
            if (string.IsNullOrWhiteSpace(data) || Plugin.JsonSerializer == null) return null;
            try
            {
                var blank = (EditableObjectBase)Activator.CreateInstance(type);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
                return blank.DeserializeFromJsonStream(stream, Plugin.JsonSerializer) as EditableOptionsBase;
            }
            catch (Exception e)
            {
                Plugin.Instance?.Logger?.Info("OptionDialog - 解析弹窗数据失败: " + e.Message);
                return null;
            }
        }
    }
}
