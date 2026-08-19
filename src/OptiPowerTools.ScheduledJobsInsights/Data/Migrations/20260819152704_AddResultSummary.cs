using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiPowerTools.ScheduledJobsInsights.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResultSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultSummary",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultSummary",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions");
        }
    }
}
