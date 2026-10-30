using System.Collections;
using System.Collections.Generic;

namespace Energy7.StationSurvey.Interface
{
    public interface ISectionService
    {
        List<Domain.Section> GetAll();
    }
}
