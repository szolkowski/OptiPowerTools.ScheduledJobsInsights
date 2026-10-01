using OptiPowerTools.ScheduledJobsInsights.Cms12.Web.Models.Pages;
using OptiPowerTools.ScheduledJobsInsights.Cms12.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace OptiPowerTools.ScheduledJobsInsights.Cms12.Web.Controllers;

public class SearchPageController : PageControllerBase<SearchPage>
{
    public ViewResult Index(SearchPage currentPage, string q)
    {
        var model = new SearchContentModel(currentPage)
        {
            Hits = Enumerable.Empty<SearchContentModel.SearchHit>(),
            NumberOfHits = 0,
            SearchServiceDisabled = true,
            SearchedQuery = q
        };

        return View(model);
    }
}
