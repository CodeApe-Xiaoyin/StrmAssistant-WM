using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 图片截取（Lite 里这个开关原本恒为 false，这里实现出来）：
    /// 没有主图的视频，用 ffmpeg 在指定百分比位置截一帧作为主图。
    /// </summary>
    public class ImageCaptureService
    {
        /// <summary>STRM 条目要截图的是它指向的远端地址，而不是那行文本。</summary>
        private static string ResolveInput(BaseItem item)
        {
            if (!item.IsShortcut || string.IsNullOrWhiteSpace(item.Path)) return item.Path;

            try
            {
                var line = File.ReadAllLines(item.Path).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                return string.IsNullOrWhiteSpace(line) ? item.Path : line.Trim();
            }
            catch
            {
                return item.Path;
            }
        }

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        public ImageCaptureService(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        public static bool NeedsCapture(BaseItem item) =>
            (item.ImageInfos == null || !System.Linq.Enumerable.Any(item.ImageInfos,
                i => i.Type == ImageType.Primary));

        public async Task<bool> CaptureAsync(BaseItem item, int positionPercent, CancellationToken token)
        {
            try
            {
                if (!NeedsCapture(item)) return false;
                if (DryRunMode.Enabled)
                {
                    _logger.Info("ImageCapture - dry run, skip capture for " + item.Name);
                    return false;
                }
                if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path)) return false;
                if (!item.RunTimeTicks.HasValue || item.RunTimeTicks.Value <= 0) return false;

                var configuredFfmpeg = Plugin.Instance?.GetPluginOptions()?.MediaInfoExtractOptions?.CustomEncoderPath;
                var ffmpeg = !string.IsNullOrWhiteSpace(configuredFfmpeg) && File.Exists(configuredFfmpeg)
                    ? configuredFfmpeg
                    : Plugin.MediaEncoder?.EncoderPath;
                if (string.IsNullOrWhiteSpace(ffmpeg) || !File.Exists(ffmpeg))
                {
                    _logger.Info("ImageCapture - ffmpeg not available");
                    return false;
                }

                var seconds = Math.Max(1, (int)(item.RunTimeTicks.Value / 10_000_000d * positionPercent / 100d));
                var target = Path.Combine(Path.GetTempPath(), "strm-capture-" + Guid.NewGuid().ToString("N") + ".jpg");

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("-y");
                psi.ArgumentList.Add("-ss");
                psi.ArgumentList.Add(seconds.ToString());
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(ResolveInput(item));
                psi.ArgumentList.Add("-frames:v");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("-q:v");
                psi.ArgumentList.Add("3");

                // 「截图后处理」：按 16:9 或 2:3 补边（不裁剪，避免丢画面）
                var postMode = Plugin.Instance?.GetPluginOptions()?.MediaInfoExtractOptions?.ImageCapturePostProcessMode;
                var padFilter = postMode switch
                {
                    "EnsureAspect16By9" => "scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:black",
                    "EnsureAspect2By3" => "scale=1000:1500:force_original_aspect_ratio=decrease,pad=1000:1500:(ow-iw)/2:(oh-ih)/2:black",
                    _ => null
                };
                if (!string.IsNullOrEmpty(padFilter))
                {
                    psi.ArgumentList.Add("-vf");
                    psi.ArgumentList.Add(padFilter);
                    _logger.Info("ImageCapture - 后处理: " + postMode);
                }

                psi.ArgumentList.Add(target);

                var stderr = string.Empty;
                using (var process = Process.Start(psi))
                {
                    if (process == null) return false;
                    stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                }

                if (!File.Exists(target) || new FileInfo(target).Length == 0)
                {
                    _logger.Info("ImageCapture - no frame produced for " + item.Name +
                                 (string.IsNullOrWhiteSpace(stderr) ? string.Empty : " | ffmpeg: " + stderr.Trim().Split('\n').Last()));
                    return false;
                }

                var providerManager = Plugin.ProviderManager;
                var fileSystem = Plugin.FileSystem;
                if (providerManager == null || fileSystem == null) return false;

                var directoryService = new DirectoryService(_logger, fileSystem);
                var libraryOptions = _libraryManager.GetLibraryOptions(item);

                await providerManager.SaveImage(item, libraryOptions, target, ImageType.Primary, null,
                    Array.Empty<long>(), directoryService, false, token).ConfigureAwait(false);

                // 「同时生成背景图」：同一帧再存一份 Backdrop
                if (postMode == "GenerateBackdrop")
                {
                    try
                    {
                        await providerManager.SaveImage(item, libraryOptions, target, ImageType.Backdrop, null,
                            Array.Empty<long>(), directoryService, false, token).ConfigureAwait(false);
                        _logger.Info("ImageCapture - 已同时生成背景图 for " + item.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.Info("ImageCapture - 背景图保存失败: " + ex.Message);
                    }
                }

                // SaveImage 只落盘，需要把图片信息写回条目，否则 UI 里仍然看不到图
                _libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.ImageUpdate);

                try { File.Delete(target); } catch { }

                _logger.Info($"ImageCapture - captured at {positionPercent}% for {item.Name}");
                return true;
            }
            catch (Exception e)
            {
                _logger.Info("ImageCapture - failed for " + item.Name + ": " + e.Message);
                return false;
            }
        }
    }
}
