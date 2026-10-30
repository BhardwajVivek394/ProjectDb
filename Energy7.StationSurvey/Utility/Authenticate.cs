using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Energy7.StationSurvey.Utility
{
    /// <summary>
    /// Redirect to Login if user session is not present.
    /// Usage: [Authenticate] on controller or action.
    /// </summary>
    public class Authenticate : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var loginUser = ClsHttpContent.GetLoginUser(filterContext.HttpContext);

            if (loginUser == null)
            {
                filterContext.Result = new RedirectToRouteResult(new RouteValueDictionary
                {
                    { "action",     "Index" },
                    { "controller", "Login" },
                    { "area",       ""      }
                });
                return;
            }

            base.OnActionExecuting(filterContext);
        }
    }
}
