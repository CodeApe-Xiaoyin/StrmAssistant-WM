using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 演练模式的全局开关。开启时所有写库/写文件动作都跳过，只记日志。
    /// </summary>
    public static class DryRunMode
    {
        public static bool Enabled =>
            Plugin.Instance?.GetPluginOptions()?.MetadataEnhanceOptions?.DryRun == true;

        /// <summary>演练模式开启时记录一条日志并返回 true，调用方应直接跳过写操作。</summary>
        public static bool Skip(string action, ILogger logger = null)
        {
            if (!Enabled) return false;
            (logger ?? Plugin.Instance?.Logger)?.Info("演练模式 - 跳过：" + action);
            return true;
        }
    }
}
