using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace StrmAssistant.EntryPoints
{
    /// <summary>
    /// 启动时检测已知的冲突插件（对应 PRO 的 ConflictPluginLoaded 提示）。
    /// 通过扫描插件目录里的程序集文件名判断，不依赖挂钩。
    /// </summary>
    public class ConflictPluginEntryPoint : IServerEntryPoint
    {
        /// <summary>与本源插件功能重叠、同时启用会造成重复处理的插件（按文件名前缀匹配）。</summary>
        private static readonly string[] KnownConflicts =
        {
            "StrmExtract",
            "StrmAssistantPro"
        };

        private readonly ILogger _logger;

        public ConflictPluginEntryPoint(ILogger logger)
        {
            _logger = logger;
        }

        public static List<string> DetectConflicts()
        {
            var found = new List<string>();

            try
            {
                // 插件目录以本插件所在目录为准（非 Docker 安装不在 /config/plugins）
                var location = typeof(Plugin).Assembly.Location;
                var dir = string.IsNullOrEmpty(location) ? "/config/plugins" : Path.GetDirectoryName(location);
                if (!Directory.Exists(dir)) return found;

                var files = Directory.GetFiles(dir, "*.dll");
                foreach (var conflict in KnownConflicts)
                {
                    if (files.Any(f =>
                            Path.GetFileName(f).StartsWith(conflict, StringComparison.OrdinalIgnoreCase)))
                    {
                        found.Add(conflict);
                    }
                }
            }
            catch
            {
                // 检测失败不影响启动
            }

            return found;
        }

        public void Run()
        {
            var conflicts = DetectConflicts();
            if (conflicts.Count == 0) return;

            _logger.Info("ConflictPlugin - detected: " + string.Join(", ", conflicts));
            _logger.Info("ConflictPlugin - 这些插件与本插件功能重叠，建议只保留一个，否则会出现重复处理。");
        }

        public void Dispose()
        {
        }
    }
}
