using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StrmAssistant.ScheduledTask
{
    /// <summary>
    /// 更新插件：从 GitHub 仓库 CodeApe-Xiaoyin/StrmAssistant-WM 的最新 Release 下载与当前 Emby 版本对应的安装包，
    /// 解压覆盖插件目录（DLL、StrmAssistantDeps、语言包），重启 Emby 后生效。
    ///
    /// 以前指向上游 sjtuross/StrmAssistant，而上游的发布文件名和本插件对不上，实际只会报「下载地址无效」。
    /// </summary>
    public class UpdatePluginTask : IScheduledTask
    {
        internal const string Repo = "CodeApe-Xiaoyin/StrmAssistant-WM";
        private static string LatestReleaseUrl => "https://api.github.com/repos/" + Repo + "/releases/latest";

        /// <summary>允许解压到插件目录的文件（防止压缩包里有 ../ 之类的路径）。</summary>
        private static readonly string[] AllowedPrefixes = { "StrmAssistantLite.dll", "StrmAssistantDeps/", "zh/", "zh-hant/" };

        private readonly ILogger _logger;
        private readonly IApplicationHost _applicationHost;
        private readonly IApplicationPaths _applicationPaths;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IActivityManager _activityManager;
        private readonly IServerApplicationHost _serverApplicationHost;

        public UpdatePluginTask(IApplicationHost applicationHost, IApplicationPaths applicationPaths,
            IJsonSerializer jsonSerializer, IActivityManager activityManager, IServerApplicationHost serverApplicationHost)
        {
            _logger = Plugin.Instance.Logger;
            _applicationHost = applicationHost;
            _applicationPaths = applicationPaths;
            _jsonSerializer = jsonSerializer;
            _activityManager = activityManager;
            _serverApplicationHost = serverApplicationHost;
        }

        public string Key => "UpdatePluginTask";

        public string Name => "更新插件";

        public string Description => "从 GitHub（" + Repo + "）下载最新版本，重启 Emby 后生效";

        public string Category => "神医助手";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerWeekly,
                DayOfWeek = (DayOfWeek)new Random().Next(7),
                TimeOfDayTicks = TimeSpan.FromMinutes(new Random().Next(24 * 4) * 15).Ticks
            };
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            progress.Report(0);
            var about = Plugin.Instance.GetPluginOptions().AboutOptions;

            try
            {
                // 走插件自己的 HTTP 客户端：填了代理地址就经代理访问 GitHub
                using var http = Common.PluginHttp.Create(TimeSpan.FromMinutes(5));
                http.DefaultRequestHeaders.UserAgent.ParseAdd("StrmAssistantLite/" + Plugin.Instance.CurrentVersion);
                if (!string.IsNullOrWhiteSpace(about.GitHubToken))
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("token", about.GitHubToken.Trim());

                var json = await http.GetStringAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
                var release = _jsonSerializer.DeserializeFromString<ApiResponseInfo>(json);
                progress.Report(10);

                var current = ParseVersion(Plugin.Instance.CurrentVersion);
                var remote = ParseVersion(release?.tag_name);
                if (remote == null) throw new Exception("最新 Release 的版本号无法识别：" + release?.tag_name);

                if (!IsNewer(current, remote))
                {
                    _logger.Info("UpdatePlugin - 已是最新版本 {0}（GitHub 最新 {1}）", current, remote);
                    _ = Plugin.NotificationApi.SendMessageToAdmins("[神医助手] 已是最新版本 " + Short(current), 1000);
                    progress.Report(100);
                    return;
                }

                var asset = PickAsset(release.assets, _serverApplicationHost.ApplicationVersion);
                if (asset == null)
                    throw new Exception("Release " + release.tag_name + " 里没有适合 Emby " + _serverApplicationHost.ApplicationVersion + " 的安装包");

                var url = asset.browser_download_url;
                if (!string.IsNullOrWhiteSpace(about.GitHubProxy))
                    url = about.GitHubProxy.Trim().TrimEnd('/') + "/" + url;
                _logger.Info("UpdatePlugin - 发现新版本 {0}，下载 {1}", remote, asset.name);

                var bytes = await http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
                progress.Report(70);

                var written = Install(bytes, _applicationPaths.PluginsPath);
                progress.Report(95);

                _logger.Info("UpdatePlugin - 已更新到 {0}，写入 {1} 个文件，重启 Emby 后生效", remote, written);
                _activityManager.Create(new ActivityLogEntry
                {
                    Name = "神医助手 已更新到 " + Short(remote) + "，重启 Emby 后生效",
                    Type = "PluginUpdateInstalled",
#if !EMBY48
                    Severity = LogSeverity.Info
#endif
                });
                _ = Plugin.NotificationApi.SendMessageToAdmins("[神医助手] 已更新到 " + Short(remote) + "，重启 Emby 后生效", 1000);
                _applicationHost.NotifyPendingRestart();
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _logger.Error("UpdatePlugin - 更新失败: {0}", e.Message);
                _activityManager.Create(new ActivityLogEntry
                {
                    Name = "神医助手 更新失败",
                    Type = "PluginUpdateFailed",
                    Overview = e.Message,
#if !EMBY48
                    Severity = LogSeverity.Error
#endif
                });
                _ = Plugin.NotificationApi.SendMessageToAdmins("[神医助手] 更新失败：" + e.Message, 1000);
            }

            progress.Report(100);
        }

        private static string Short(Version v) => v == null ? "?" : v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : v.ToString();

        /// <summary>v2.2.0 / 2.2.0 / 2.2.0.0 → Version；认不出返回 null。</summary>
        internal static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var m = Regex.Match(tag, @"\d+(\.\d+){1,3}");
            return m.Success && Version.TryParse(m.Value, out var v) ? v : null;
        }

        /// <summary>比较前三位（2.2.0 与 2.2.0.0 视为同一版本）。</summary>
        internal static bool IsNewer(Version current, Version remote)
        {
            static Version Norm(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            return current == null || Norm(remote).CompareTo(Norm(current)) > 0;
        }

        /// <summary>
        /// 选安装包：优先文件名里带当前 Emby 主次版本号的（StrmAssistantLite-emby4.10.zip），
        /// 没有就用不带版本号的通用包（StrmAssistantLite.zip）。
        /// </summary>
        internal static ApiAssetInfo PickAsset(IEnumerable<ApiAssetInfo> assets, Version emby)
        {
            var zips = (assets ?? Enumerable.Empty<ApiAssetInfo>())
                .Where(a => a?.name != null && a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            Uri.IsWellFormedUriString(a.browser_download_url, UriKind.Absolute))
                .ToList();
            if (emby != null)
            {
                var tag = "emby" + emby.Major + "." + emby.Minor + ".";
                var exact = zips.FirstOrDefault(a => (a.name.Replace(".zip", ".") ).IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0);
                if (exact != null) return exact;
            }

            return zips.FirstOrDefault(a => a.name.IndexOf("emby", StringComparison.OrdinalIgnoreCase) < 0);
        }

        /// <summary>
        /// 解压到插件目录。每个文件先写临时文件再改名覆盖：正在使用的 DLL 不能原地截断重写（Linux 上会让运行中的进程读到坏数据）。
        /// 返回写入的文件数。
        /// </summary>
        internal static int Install(byte[] zipBytes, string pluginsPath)
        {
            var root = Path.GetFullPath(pluginsPath);
            var count = 0;
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            if (!entries.Any(e => e.FullName.Replace('\\', '/') == "StrmAssistantLite.dll"))
                throw new Exception("安装包里没有 StrmAssistantLite.dll");

            foreach (var entry in entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!AllowedPrefixes.Any(p => name == p || (p.EndsWith("/") && name.StartsWith(p, StringComparison.Ordinal))))
                    continue;

                var target = Path.GetFullPath(Path.Combine(root, name));
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                var temp = target + ".update-tmp";
                using (var input = entry.Open())
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
                {
                    input.CopyTo(output);
                }

                File.Move(temp, target, true);
                count++;
            }

            return count;
        }

        internal class ApiResponseInfo
        {
            public string tag_name { get; set; }

            public List<ApiAssetInfo> assets { get; set; }
        }

        internal class ApiAssetInfo
        {
            public string name { get; set; }

            public string browser_download_url { get; set; }
        }
    }
}
