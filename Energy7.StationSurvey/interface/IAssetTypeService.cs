using System.Collections.Generic;

namespace Energy7.StationSurvey.Interface
{
    public interface IAssetTypeService
    {
        List<Domain.AssetType> GetAll();

        List<Domain.AssetType> GetBy(int siteId);
    }
}
