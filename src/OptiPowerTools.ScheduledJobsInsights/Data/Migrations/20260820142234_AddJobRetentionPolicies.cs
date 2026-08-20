using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiPowerTools.ScheduledJobsInsights.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRetentionPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobRetentionPolicies",
                schema: "scheduled_jobs_insights",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobTypeName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RetentionDays = table.Column<int>(type: "int", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRetentionPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobExecutions_JobTypeName_StartedAt",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions",
                columns: new[] { "JobTypeName", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobRetentionPolicies_JobTypeName",
                schema: "scheduled_jobs_insights",
                table: "JobRetentionPolicies",
                column: "JobTypeName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobRetentionPolicies",
                schema: "scheduled_jobs_insights");

            migrationBuilder.DropIndex(
                name: "IX_JobExecutions_JobTypeName_StartedAt",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions");
        }
    }
}
