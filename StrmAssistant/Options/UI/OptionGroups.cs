using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Editors;
using MediaBrowser.Model.GenericEdit;

namespace StrmAssistant.Options.UI
{
    /// <summary>一个分组：标题 + 属性名。标题为 null 时这些属性（通常是子对象）直接放在页面顶层。</summary>
    public sealed class OptionGroup
    {
        public OptionGroup(string title, params string[] names)
        {
            Title = title;
            Names = names;
        }

        public string Title { get; }
        public string Description { get; set; } = string.Empty;
        public string[] Names { get; }
    }

    /// <summary>
    /// 把一个选项页的平铺属性按分组重新排成带标题的小节（与 MediaInfoKeeper 的做法一致：
    /// 先让 Emby 按属性生成编辑器，再把编辑器挪进 EditorGroup）。没有列进任何分组的属性放在最后，不丢失。
    /// </summary>
    public static class OptionGroups
    {
        public static IEditObjectContainer Apply(IEditObjectContainer container, params OptionGroup[] groups)
        {
            if (!(container is EditObjectContainer edit)) return container;
            var root = edit.EditorRoot;
            var items = root?.EditorItems;
            if (items == null || items.Length == 0) return container;

            var lookup = new Dictionary<string, EditorBase>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var key = item.Name ?? item.Id;
                if (!string.IsNullOrEmpty(key) && !lookup.ContainsKey(key)) lookup[key] = item;
            }

            var result = new List<EditorBase>();
            var index = 0;
            foreach (var group in groups)
            {
                var members = group.Names.Where(lookup.ContainsKey).Select(n => lookup[n]).ToList();
                foreach (var n in group.Names) lookup.Remove(n);
                if (members.Count == 0) continue;

                if (group.Title == null)
                {
                    result.AddRange(members);
                    continue;
                }

                index++;
                result.Add(new EditorGroup(group.Title, members.ToArray(), "grp" + index, root.Id, null)
                {
                    Description = group.Description
                });
            }

            // 没分组的按原顺序放最后
            result.AddRange(items.Where(i => lookup.ContainsKey(i.Name ?? i.Id ?? string.Empty)));

            root.EditorItems = result.ToArray();
            return container;
        }
    }
}
