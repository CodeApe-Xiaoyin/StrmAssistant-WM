using System;
using System.Collections.Generic;
using System.Reflection;
using Emby.Web.GenericEdit.Elements;

namespace StrmAssistant.Common
{
    /// <summary>
    /// 选项列表的中文显示名。
    ///
    /// 之前这些列表用 `item.ToString()` 直接当名字，界面就显示成 IgnoreFileChange / NoOverview 这种英文枚举名；
    /// 更糟的是保存后 UI 回传的列表项只有 Value、没有 Name，页面就整片显示 "undefined"。
    /// 这里统一按 Value 给出中文名，并在保存合并后按 Value 重新补齐 Name。
    /// </summary>
    internal static class OptionLabels
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            // 代理
            ["Http"] = "HTTP",
            ["Socks5"] = "SOCKS5",
            ["Global"] = "全局（含 Emby 自身请求）",
            ["PluginOnly"] = "仅插件请求",

            // 多版本
            ["SystemDefault"] = "跟随系统默认",
            ["MediaInfo"] = "媒体信息",
            ["MediaInfoExtended"] = "媒体信息（详细）",
            ["FileNameWithoutExtension"] = "文件名（不含扩展名）",
            ["HighToLowQuality"] = "画质从高到低",
            ["LowToHighQuality"] = "画质从低到高",
            ["PathAscending"] = "路径升序",
            ["PathDescending"] = "路径降序",

            // 元数据刷新规则
            ["NoOverview"] = "缺简介",
            ["NoImage"] = "缺图片",
            ["NonChineseNameOverview"] = "非中文名称或简介",
            ["DefaultEpisodeName"] = "默认分集名",
            ["ReplaceCapturedImage"] = "替换自动截图",
            ["NonChineseOverview"] = "非中文简介",

            // 独占控制
            ["IgnoreFileChange"] = "忽略文件变更",
            ["ExtractOnFileChange"] = "文件变更时提取",
            ["CatchAllAllow"] = "兜底规则：允许",
            ["CatchAllAllowChapterImage"] = "兜底规则：允许章节图",
            ["CatchAllBlock"] = "兜底规则：阻止",
            ["IgnoreExtSubChange"] = "忽略外挂字幕变更",
            ["NoPersistIntegration"] = "不接入持久化",
            ["NoNfoSaverOptimization"] = "关闭 NFO 保存优化",
            ["ExtractAlternative"] = "提取替代版本",

            // 持久化模式
            ["None"] = "关闭",
            ["Default"] = "写入 JSON",
            ["Restore"] = "从 JSON 还原",


            // 追更任务（MediaInfo 与多版本共用同一个名字）
            ["Fingerprint"] = "片头声纹",
            ["IntroSkip"] = "片头跳过",
            ["EpisodeRefresh"] = "分集刷新",
            ["PersonRefresh"] = "演员刷新",

            // 片头标记偏好
            ["ResetAndOverwrite"] = "重置并覆盖",
            ["NoDetectionButReset"] = "不检测只重置",


            // 演员刷新规则
            ["DefaultMetadataRefresh"] = "默认元数据刷新",
            ["ExcludeIfChinese"] = "中文则跳过",
            ["ExcludeAdultMetadata"] = "排除成人元数据",
            ["FullMetadataRefresh"] = "完整元数据刷新",
            ["DefaultImageRefresh"] = "默认图片刷新",
            ["FullImageRefresh"] = "完整图片刷新",
            ["DownloadImagesInAdvance"] = "预先下载图片",

            // 语言
            ["zh-CN"] = "简体中文",
            ["zh-TW"] = "繁体中文（台湾）",
            ["zh-HK"] = "繁体中文（香港）",
            ["en"] = "英语",
            ["ja"] = "日语",
            ["ko"] = "韩语",
            ["fr"] = "法语",
            ["de"] = "德语",
            ["es"] = "西班牙语",
            ["it"] = "意大利语",
            ["ru"] = "俄语",
            ["pt"] = "葡萄牙语",
            ["th"] = "泰语",
            ["vi"] = "越南语",

            // 搜索增强：索引模式 / 条目类型 / 调优项
            ["Full"] = "完整",
            ["Lite"] = "轻量",
            ["Movie"] = "电影",
            ["Collection"] = "合集",
            ["Series"] = "剧集",
            ["Season"] = "季",
            ["Episode"] = "分集",
            ["Person"] = "人物",
            ["LiveTv"] = "电视直播",
            ["Music"] = "音乐",
            ["MusicAlbum"] = "音乐专辑",
            ["MusicArtist"] = "音乐艺人",
            ["Playlist"] = "播放列表",
            ["Video"] = "视频",
            ["Photo"] = "照片",
            ["PhotoAlbum"] = "相册",
            ["Genre"] = "类型",
            ["Book"] = "书籍",
            ["Game"] = "游戏",
            ["Trailer"] = "预告片",
            ["Tag"] = "标签",
            ["Studio"] = "工作室",
            ["ExcludeOriginalTitle"] = "不搜索原始标题",
            ["TraditionalToSimplifiedChinese"] = "繁体转简体匹配",
            ["MixedChineseVariants"] = "简繁混合匹配",
            ["SuppressSearchSuggestions"] = "隐藏搜索建议",
            ["ShowMediaInfo"] = "搜索结果显示媒体信息",

            // 豆瓣辅助刮削项
            ["Overview"] = "简介",
            ["Rating"] = "评分",
            ["Name"] = "标题",

            // 其他
            ["AppendOnly"] = "仅追加",
        };

        public static string Label(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return Map.TryGetValue(value, out var label) ? label : value;
        }

        /// <summary>
        /// 当前显示名是否"坏掉"、需要按 Value 重新补：空 / "undefined" / 直接等于英文 Value。
        /// 已经是有意义的名字（媒体库名、用户名、资源文件里的文案）一律不动——
        /// 之前的写法无条件用 Label(Value) 覆盖，作用到媒体库列表时会把库名替换成库 Id。
        /// </summary>
        private static bool NeedsRepair(string current, string raw)
        {
            if (string.IsNullOrWhiteSpace(current) || current == "undefined") return true;
            return !string.IsNullOrEmpty(raw) && string.Equals(current, raw, StringComparison.OrdinalIgnoreCase);
        }

        private static void RepairItem(object item)
        {
            if (item == null) return;
            var t = item.GetType();
            var valueProp = t.GetProperty("Value");
            if (valueProp == null) return;

            // EditorRadioOption / EditorSelectOption 的 IsEnabled 默认是 false：
            // 不设成 true 的话，单选组渲染出来每个选项都是 disabled，点不动。
            var enabledProp = t.GetProperty("IsEnabled");
            if (enabledProp != null && enabledProp.CanWrite && enabledProp.PropertyType == typeof(bool)
                && !(bool)enabledProp.GetValue(item))
                enabledProp.SetValue(item, true);

            var raw = valueProp.GetValue(item)?.ToString();
            var label = Label(raw);
            if (string.IsNullOrEmpty(label)) return;

            // EditorSelectOption 用 Name；EditorRadioOption 用 PrimaryText（单选组就是读它）
            foreach (var propName in new[] { "Name", "PrimaryText" })
            {
                var prop = t.GetProperty(propName);
                if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(string)) continue;
                var current = prop.GetValue(item) as string;
                if (NeedsRepair(current, raw) && !string.Equals(current, label, StringComparison.Ordinal))
                    prop.SetValue(item, label);
            }
        }

        /// <summary>就地修正一个列表里的显示名（列表来自配置文件时尤其需要）。</summary>
        public static void RepairList(System.Collections.IEnumerable list)
        {
            if (list == null) return;
            foreach (var item in list) RepairItem(item);
        }

        /// <summary>
        /// 保存合并后 / 读取配置时调用：遍历选项对象（含 PluginOptions 下一层的各分页选项），
        /// 把所有选项列表里坏掉的显示名按 Value 补齐。
        /// </summary>
        public static void Repair(object options) => Repair(options, 0);

        private static void Repair(object options, int depth)
        {
            if (options == null || depth > 2) return;

            foreach (var prop in options.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
                var type = prop.PropertyType;
                if (type == typeof(string) || type.IsValueType) continue;

                object value;
                try { value = prop.GetValue(options); }
                catch { continue; }
                if (value == null) continue;

                if (value is System.Collections.IEnumerable list)
                {
                    RepairList(list);
                }
                else if (value is Emby.Web.GenericEdit.EditableOptionsBase)
                {
                    Repair(value, depth + 1);
                }
            }
        }
    }
}
