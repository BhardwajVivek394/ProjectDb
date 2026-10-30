using Domain;
using Microsoft.AspNetCore.Http;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// Session helpers — replaces the old HttpContext.Current.Session pattern.
    /// Pass HttpContext directly from controllers or filters.
    /// </summary>
    public static class ClsHttpContent
    {
        private const string KEY_USER = "LoginUser";

        public static User? GetLoginUser(HttpContext context)
            => context.Session.GetObject<User>(KEY_USER);

        public static void SetLoginUser(HttpContext context, User user)
            => context.Session.SetObject(KEY_USER, user);

        public static void ClearLoginUser(HttpContext context)
            => context.Session.Remove(KEY_USER);
    }
}
