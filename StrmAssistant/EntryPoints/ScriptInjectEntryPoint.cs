using System;
using System.Linq;
using System.IO;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.EntryPoints
{
    /// <summary>
    /// 启动时把前端脚本注入 Emby 的 index.html（对应 PRO 的 ScriptInject）。
    /// 只改动一次，带标记位，重复启动不会重复插入。
    /// </summary>
    public class ScriptInjectEntryPoint : IServerEntryPoint
    {
        private const string Marker = "<!-- StrmAssistant-Inject -->";
        private static readonly string Tag = Marker + "<script src=\"/emby/StrmAssistant/Script?v=" +
                                             Web.Service.ScriptInjectService.ScriptVersion + "\"></script>";

        /// <summary>
        /// Emby 网页目录：优先用 Emby 自己报告的程序目录（Windows、原生 Linux 包都不在 /system），
        /// Docker 镜像的 /system 作为兜底。以前写死 /system，非 Docker 环境下前端功能全部静默失效。
        /// </summary>
        private static string[] CandidatePaths
        {
            get
            {
                var dirs = new System.Collections.Generic.List<string>();
                try
                {
                    var system = Plugin.Instance?.ApplicationPaths?.ProgramSystemPath;
                    if (!string.IsNullOrWhiteSpace(system)) dirs.Add(Path.Combine(system, "dashboard-ui"));
                }
                catch
                {
                    // 拿不到就只用兜底路径
                }

                dirs.Add("/system/dashboard-ui");

                return dirs.Distinct(StringComparer.Ordinal)
                    .SelectMany(d => new[] { Path.Combine(d, "index.html"), Path.Combine(d, "index.html.template") })
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
        }

        private readonly ILogger _logger;

        public ScriptInjectEntryPoint(ILogger logger)
        {
            _logger = logger;
        }

        public void Run()
        {
            if (TryPatch()) return;

            // 某些环境下启动瞬间 /system 还是只读的，稍后重试（最多约 2 分钟）
            System.Threading.Tasks.Task.Run(async () =>
            {
                for (var attempt = 1; attempt <= 6; attempt++)
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                    if (TryPatch())
                    {
                        _logger.Info("ScriptInject - patched on retry " + attempt);
                        return;
                    }
                }

                _logger.Info("ScriptInject - giving up after retries (UI features will not load)");
            });
        }

        private bool TryPatch()
        {
            var patchedAny = false;

            try
            {
                foreach (var path in CandidatePaths)
                {
                    if (!File.Exists(path)) continue;

                    var html = File.ReadAllText(path);

                    // 已有旧注入（可能是旧版本脚本地址）时先移除，保证地址可更新
                    var markerIndex = html.IndexOf(Marker, StringComparison.Ordinal);
                    if (markerIndex >= 0)
                    {
                        var endIndex = html.IndexOf("</script>", markerIndex, StringComparison.Ordinal);
                        if (endIndex > 0)
                        {
                            html = html.Remove(markerIndex, endIndex + 9 - markerIndex);
                        }
                    }

                    var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    var patched = index >= 0
                        ? html.Insert(index, Tag + Environment.NewLine)
                        : html + Tag;

                    File.WriteAllText(path, patched);
                    _logger.Info("ScriptInject - patched " + path);
                    patchedAny = true;
                }
            }
            catch (Exception e)
            {
                _logger.Info("ScriptInject - failed: " + e.Message);
            }

            return patchedAny;
        }

        public void Dispose()
        {
        }
    }
}
