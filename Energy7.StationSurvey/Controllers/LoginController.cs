using Domain;
using Energy7.StationSurvey.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Text;

namespace Energy7.StationSurvey.Controllers
{
    public class LoginController : Controller
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public LoginController(IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
        }

        // GET: /Login
        public IActionResult Index()
        {
            var mLogin = new Login();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(HttpContext);

                // Log out existing session
                if (loginUser != null)
                {
                    using var hcf = new HttpClientHelper(_config, _httpContextAccessor);
                    var history = new LoginHistory
                    {
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                        UserId = loginUser.Id
                    };
                    var body = new StringContent(JsonConvert.SerializeObject(history), Encoding.UTF8, "application/json");
                    hcf.Client.PostAsync("Login/LogOut", body).Wait();

                    ClsHttpContent.ClearLoginUser(HttpContext);
                }

                // Pre-fill from Remember Me cookie
                if (Request.Cookies["rm_email"] != null)
                {
                    mLogin.EmailAddress = Request.Cookies["rm_email"];
                    mLogin.Password = Request.Cookies["rm_password"];
                    mLogin.RememberMe = true;
                }

                TempData.Clear();
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }

            return View(mLogin);
        }

        // POST: /Login
        [HttpPost]
        public IActionResult Index(Login mLogin)
        {
            mLogin.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            mLogin.Source = "Website";
            mLogin.MacAddress = Request.Cookies["deviceId"] ?? Guid.NewGuid().ToString();

            // Clear errors for fields we set manually
            ModelState.Remove(nameof(mLogin.IpAddress));
            ModelState.Remove(nameof(mLogin.Source));
            ModelState.Remove(nameof(mLogin.MacAddress));
            if (!ModelState.IsValid)
                return View(mLogin);

            try
            {
                // ── IP Address ────────────────────────────────────────────────
                mLogin.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

                // ── Source ────────────────────────────────────────────────────
                mLogin.Source = "Website";

                // ── MAC / Device ID ───────────────────────────────────────────
                if (Request.Cookies["deviceId"] != null)
                {
                    mLogin.MacAddress = Request.Cookies["deviceId"];
                }
                else
                {
                    var newDeviceId = Guid.NewGuid().ToString();
                    Response.Cookies.Append("deviceId", newDeviceId, new CookieOptions
                    {
                        Expires = DateTimeOffset.Now.AddYears(1),
                        HttpOnly = true
                    });
                    mLogin.MacAddress = newDeviceId;
                }

                // ── Call API ──────────────────────────────────────────────────
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor);
                var body = new StringContent(JsonConvert.SerializeObject(mLogin), Encoding.UTF8, "application/json");
                var response = hcf.Client.PostAsync("Login/Authenticate", body).Result;

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    ViewBag.Error = "Internal server error";
                    return View(mLogin);
                }

                var mUser = JsonConvert.DeserializeObject<User>(
                                response.Content.ReadAsStringAsync().Result);

                // ── Blocked IP ────────────────────────────────────────────────
                if (mUser == null || mUser.IsBlockIp)
                {
                    ViewBag.Error = "Invalid username or password.";
                    return View(mLogin);
                }

                // ── MAC not approved ──────────────────────────────────────────
                if (!mUser.IsMacIdApproved)
                {
                    ViewBag.Error = "Suspicious login detected! Kindly contact admin.";
                    return View(mLogin);
                }

                // ── Invalid user ──────────────────────────────────────────────
                if (mUser.Id <= 0)
                {
                    ViewBag.Error = "Invalid username or password.";
                    return View(mLogin);
                }

                // ── Inactive user ─────────────────────────────────────────────
                if (!mUser.IsActive)
                {
                    ViewBag.Error = "You are not authorized to access the portal.";
                    return View(mLogin);
                }

                // ── Remember Me ───────────────────────────────────────────────
                if (mLogin.RememberMe)
                {
                    var opts = new CookieOptions { Expires = DateTimeOffset.Now.AddDays(15), HttpOnly = true };
                    Response.Cookies.Append("rm_email", mLogin.EmailAddress ?? "", opts);
                    Response.Cookies.Append("rm_password", mLogin.Password ?? "", opts);
                }
                else
                {
                    Response.Cookies.Delete("rm_email");
                    Response.Cookies.Delete("rm_password");
                }

                // ── Save user to session ──────────────────────────────────────
                ClsHttpContent.SetLoginUser(HttpContext, mUser);

                return RedirectToAction("Index", "Station");
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View(mLogin);
            }
        }

        // GET: /Login/Logout
        public IActionResult Logout()
        {
            ClsHttpContent.ClearLoginUser(HttpContext);
            return RedirectToAction("Index", "Login");
        }
    }
}
