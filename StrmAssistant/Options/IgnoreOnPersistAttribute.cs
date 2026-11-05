using System;

namespace StrmAssistant.Options
{
    /// <summary>
    /// 移植自 PRO 的 StrmAssistant.Options.IgnoreOnPersistAttribute。
    /// 标记该属性不参与持久化。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class IgnoreOnPersistAttribute : Attribute
    {
    }
}
