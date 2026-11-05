using System;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    public class DependencyCheckService : IService
    {
        public object Get(DependencyCheckApi request)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>();

            try
            {
                result["pinyin"] = Common.SortNameHelper.BuildSortName("沙丘");
            }
            catch (Exception e)
            {
                result["pinyin"] = "FAIL " + e.GetType().Name + ": " + e.Message;
            }

            try
            {
                result["traditional"] = Common.ChineseTextHelper.ToTraditional("简体中文测试");
            }
            catch (Exception e)
            {
                result["traditional"] = "FAIL " + e.GetType().Name + ": " + e.Message;
            }

            try
            {
                var conflicts = EntryPoints.ConflictPluginEntryPoint.DetectConflicts();
                result["conflictPlugins"] = conflicts.Count == 0 ? "none" : string.Join(",", conflicts);
            }
            catch (Exception e)
            {
                result["conflictPlugins"] = "FAIL " + e.Message;
            }

            return result;
        }
    }
}
