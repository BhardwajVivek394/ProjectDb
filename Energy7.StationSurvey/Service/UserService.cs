using Domain;
using Energy7.StationSurvey.Interface;
using Energy7.StationSurvey.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;

namespace Energy7.StationSurvey.Service
{
    public class UserService : IUserService
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public UserService(IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
        }

        public User GetBy(int id)
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync($"User/GetUserById/{id}").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    return JsonConvert.DeserializeObject<User>(jsonString) ?? new User();
            }
            catch { /* silent */ }

            return new User();
        }

        public List<User> GetSiteKeepingUser()
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("User/GetSiteKeepingUser").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var mUsers = JsonConvert.DeserializeObject<List<User>>(jsonString) ?? new();
                    mUsers.ForEach(u => u.Name = $"{u.FirstName} {u.LastName}");
                    return mUsers;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("UserService.GetSiteKeepingUser failed", ex);
            }
            return new List<User>();
        }

        public List<User> GetAuditUser()
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("User/GetAuditUser").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var mUsers = JsonConvert.DeserializeObject<List<User>>(jsonString) ?? new();
                    mUsers.ForEach(u => u.Name = u.FirstName);
                    return mUsers;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("UserService.GetAuditUser failed", ex);
            }
            return new List<User>();
        }
    }
}