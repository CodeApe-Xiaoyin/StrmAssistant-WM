using System;
using System.IO;
using System.Reflection;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 「自定义 ffmpeg 路径」：把用户填的路径写进 Emby 自己的 FFmpeg 配置，
    /// 这样截图（以及其它走 ffmpeg 的地方）都会用它。
    /// PRO 的做法同样是设置 FfmpegConfiguration.UseCustomEncoderPath / CustomEncoderPath。
    /// </summary>
    public static class FfmpegPathOverride
    {
        /// <summary>
        /// 启动时 FfmpegConfiguration 往往还没初始化（生产实测为 null），
        /// 所以后台重试若干次，直到能拿到配置再写入。
        /// </summary>
        public static void ApplyWithRetry(ILogger logger, int attempts = 6, int delaySeconds = 20)
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                for (var i = 1; i <= attempts; i++)
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(delaySeconds))
                        .ConfigureAwait(false);

                    var result = Apply(logger);

                    if (result != null && !result.Contains("不可用"))
                    {
                        logger?.Info("FfmpegPath - 第 " + i + " 次重试成功: " + result);
                        return;
                    }

                    if (i == attempts)
                        logger?.Info("FfmpegPath - 重试 " + attempts + " 次仍未拿到 FfmpegConfiguration（最后: " + result + "）");
                }
            });
        }

        public static string Apply(ILogger logger = null)
        {
            try
            {
                var options = Plugin.Instance?.GetPluginOptions()?.MediaInfoExtractOptions;
                var customPath = options?.CustomEncoderPath?.Trim();

                var manager = Plugin.FfmpegManager;
                if (manager == null) return "FfmpegManager 不可用";

                var config = manager.FfmpegConfiguration;
                if (config == null) return "FfmpegConfiguration 不可用";

                var useProp = config.GetType().GetProperty("UseCustomEncoderPath");
                var pathProp = config.GetType().GetProperty("CustomEncoderPath");
                if (useProp == null || pathProp == null) return "FfmpegConfiguration 缺少自定义路径属性";

                var enable = !string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath);

                if (!string.IsNullOrWhiteSpace(customPath) && !enable)
                {
                    logger?.Info("FfmpegPath - 路径不存在，保持默认: " + customPath);
                }

                useProp.SetValue(config, enable);
                pathProp.SetValue(config, enable ? customPath : null);

                var applied = "custom=" + (useProp.GetValue(config)?.ToString() ?? "?") +
                              " path=" + (pathProp.GetValue(config)?.ToString() ?? "(empty)");

                logger?.Info("FfmpegPath - 已应用: " + applied);
                return applied;
            }
            catch (Exception e)
            {
                logger?.Info("FfmpegPath - 应用失败: " + e.Message);
                return "失败: " + e.Message;
            }
        }
    }
}
