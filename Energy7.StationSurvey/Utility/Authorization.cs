using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// Redirect to Login (with returnUrl) if user session is not present.
    /// Usage: [Authorization] on controller or action.
    /// </summary>
    public class Authorization : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var loginUser = ClsHttpContent.GetLoginUser(filterContext.HttpContext);

            if (loginUser == null)
            {
                var request   = filterContext.HttpContext.Request;
                var returnUrl = $"{request.Path}{request.QueryString}";

                filterContext.Result = new RedirectToRouteResult(new RouteValueDictionary
                {
                    { "action",     "Index" },
                    { "controller", "Login" },
                    { "returnUrl",  returnUrl }
                });
                return;
            }

            base.OnActionExecuting(filterContext);
        }
    }
}
