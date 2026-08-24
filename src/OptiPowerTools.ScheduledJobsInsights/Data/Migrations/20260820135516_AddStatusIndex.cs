using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiPowerTools.ScheduledJobsInsights.Data.Migrations
{
    /// <inheritdoc />
    internal partial class AddStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JobExecutions_Status_StartedAt_Id",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions",
                columns: new[] { "Status", "StartedAt", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobExecutions_Status_StartedAt_Id",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions");
        }
    }
}
