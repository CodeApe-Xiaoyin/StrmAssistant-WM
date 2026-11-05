using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;

namespace StrmAssistant.Web.Service
{
    /// <summary>移植自 PRO「强制媒体库顺序」：把第一个管理员的 OrderedViews 复制给所有用户。</summary>
    public class EnforceLibraryOrderService : IService
    {
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public EnforceLibraryOrderService(IUserManager userManager, ILogger logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        public object Post(EnforceLibraryOrderApi request)
        {
            if (!Plugin.Instance.GetPluginOptions().ExperienceEnhanceOptions.UIFunctionOptions.EnforceLibraryOrder)
            {
                _logger.Info("EnforceLibraryOrder skipped: option disabled");
                return new { updated = 0, reason = "disabled" };
            }

            var users = _userManager.GetUsers(new MediaBrowser.Model.Querying.UserQuery()).Items.ToList();
            var admin = users.FirstOrDefault(u =>
                u.Policy != null && u.Policy.IsAdministrator &&
                u.Configuration != null && u.Configuration.OrderedViews != null &&
                u.Configuration.OrderedViews.Length > 0);

            if (admin == null)
            {
                _logger.Info("EnforceLibraryOrder skipped: no admin with ordered views");
                return new { updated = 0, reason = "no-admin-order" };
            }

            var ordered = admin.Configuration.OrderedViews.ToArray();
            var updated = 0;

            foreach (var user in users.Where(u => !string.Equals(u.Id.ToString(), admin.Id.ToString())))
            {
                if (user.Configuration == null) continue;

                user.Configuration.OrderedViews = ordered.ToArray();
                _userManager.UpdateUser(user);
                updated++;
            }

            _logger.Info($"EnforceLibraryOrder applied from {admin.Name} to {updated} users");
            return new { updated, admin = admin.Name };
        }
    }
}
