using Energy7.StationSurvey.Interface;
using Energy7.StationSurvey.Service;
using Energy7.StationSurvey.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;

namespace E7MRIWeb.Service
{
    public class AssetTypeService : IAssetTypeService
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        public AssetTypeService(IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
        }
        public List<Domain.AssetType> GetAll()
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            try
            {
                var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                {
                    var response = hcf.Client.GetAsync($"AssetType/GetAllAssestType").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetTypes = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return mAssetTypes;
        }

        public List<Domain.AssetType> GetBy(int siteId)
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            if (siteId > 0)
            {
                try
                {
                    var loginUser = ClsHttpContent.GetLoginUser(_httpContextAccessor.HttpContext!);

                using var hcf = new HttpClientHelper(_config, _httpContextAccessor, token: loginUser!.Token);
                    {
                        var response = hcf.Client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                        var jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mAssetTypes = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }

            return mAssetTypes;
        }
    }
}