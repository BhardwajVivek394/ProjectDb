using System.Collections.Generic;

namespace Energy7.StationSurvey.Interface
{
    public interface IUserService
    {
        Domain.User GetBy(int id);

        List<Domain.User> GetSiteKeepingUser();

        List<Domain.User> GetAuditUser();
    }
}
