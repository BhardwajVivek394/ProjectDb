using System.Collections.Generic;

namespace Energy7.StationSurvey.Interface
{
    public interface IZoneService
    {
        List<Domain.Zone> GetAll();

        List<Domain.Zone> GetAllZones();

        Domain.Zone GetZoneById(int id);
    }
}
