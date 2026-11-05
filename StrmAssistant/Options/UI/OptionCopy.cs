using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace StrmAssistant.Options.UI
{
    /// <summary>按属性名在两个设置对象之间复制值（弹窗 ⇄ 真正保存的设置）。</summary>
    public static class OptionCopy
    {
        /// <summary>界面上看不到的属性（[Browsable(false)]）。</summary>
        public static bool IsHidden(PropertyInfo p) =>
            p.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false;

        /// <summary>把 source 里所有同名、可写、类型兼容的属性复制到 target（含隐藏的下拉列表）。</summary>
        public static void CopyAll(object source, object target) => Copy(source, target, false);

        /// <summary>只复制界面上可编辑的属性（弹窗 → 设置时用：下拉列表等辅助数据不往回写）。</summary>
        public static void CopyEditable(object source, object target) => Copy(source, target, true);

        private static void Copy(object source, object target, bool editableOnly)
        {
            if (source == null || target == null) return;
            var sourceProps = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToDictionary(p => p.Name);

            // 以弹窗类型声明的属性为准：弹窗 → 设置时用 source 的声明，设置 → 弹窗时用 target 的声明
            var declaring = editableOnly ? source.GetType() : target.GetType();
            foreach (var declared in declaring.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (editableOnly && IsHidden(declared)) continue;
                var to = target.GetType().GetProperty(declared.Name, BindingFlags.Public | BindingFlags.Instance);
                if (to == null || !to.CanWrite || to.GetIndexParameters().Length != 0) continue;
                if (!sourceProps.TryGetValue(declared.Name, out var from)) continue;

                var value = from.GetValue(source);
                if (value != null && !to.PropertyType.IsInstanceOfType(value)) continue;
                if (value == null && to.PropertyType.IsValueType &&
                    System.Nullable.GetUnderlyingType(to.PropertyType) == null) continue;
                to.SetValue(target, value);
            }
        }
    }
}
