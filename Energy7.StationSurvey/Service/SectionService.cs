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
    public class SectionService : ISectionService
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SectionService(IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
        }

        public List<Section> GetAll()
        {
            var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);
            try
            {
                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                var response = hcf.Client.GetAsync("Section/GetAll").Result;
                var jsonString = response.Content.ReadAsStringAsync().Result;

                if (response.StatusCode == HttpStatusCode.OK)
                    return JsonConvert.DeserializeObject<List<Section>>(jsonString) ?? new();
            }
            catch (Exception ex)
            {
                throw new Exception("SectionService.GetAll failed", ex);
            }
            return new List<Section>();
        }
    }
}