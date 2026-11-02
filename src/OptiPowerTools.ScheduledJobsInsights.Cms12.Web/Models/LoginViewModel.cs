using System.ComponentModel.DataAnnotations;

namespace OptiPowerTools.ScheduledJobsInsights.Cms12.Web.Models;

public class LoginViewModel
{
    [Required]
    public string Username { get; set; }

    [Required]
    public string Password { get; set; }
}
