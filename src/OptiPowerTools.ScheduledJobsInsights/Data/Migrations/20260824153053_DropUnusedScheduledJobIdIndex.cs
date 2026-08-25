using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiPowerTools.ScheduledJobsInsights.Data.Migrations
{
    /// <inheritdoc />
    internal partial class DropUnusedScheduledJobIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobExecutions_ScheduledJobId",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JobExecutions_ScheduledJobId",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions",
                column: "ScheduledJobId");
        }
    }
}
