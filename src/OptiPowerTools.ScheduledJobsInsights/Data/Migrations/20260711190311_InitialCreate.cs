using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptiPowerTools.ScheduledJobsInsights.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduled_jobs_insights");

            migrationBuilder.CreateTable(
                name: "JobExecutions",
                schema: "scheduled_jobs_insights",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduledJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    JobTypeName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ResultMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExceptionMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExceptionStackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputDataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MachineName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobLogEntries",
                schema: "scheduled_jobs_insights",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    Source = table.Column<byte>(type: "tinyint", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobLogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobLogEntries_JobExecutions_JobExecutionId",
                        column: x => x.JobExecutionId,
                        principalSchema: "scheduled_jobs_insights",
                        principalTable: "JobExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobMetrics",
                schema: "scheduled_jobs_insights",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobMetrics_JobExecutions_JobExecutionId",
                        column: x => x.JobExecutionId,
                        principalSchema: "scheduled_jobs_insights",
                        principalTable: "JobExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobExecutions_ScheduledJobId",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions",
                column: "ScheduledJobId");

            migrationBuilder.CreateIndex(
                name: "IX_JobExecutions_StartedAt_Id",
                schema: "scheduled_jobs_insights",
                table: "JobExecutions",
                columns: new[] { "StartedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_JobLogEntries_JobExecutionId_Sequence",
                schema: "scheduled_jobs_insights",
                table: "JobLogEntries",
                columns: new[] { "JobExecutionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobMetrics_JobExecutionId_Name",
                schema: "scheduled_jobs_insights",
                table: "JobMetrics",
                columns: new[] { "JobExecutionId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobLogEntries",
                schema: "scheduled_jobs_insights");

            migrationBuilder.DropTable(
                name: "JobMetrics",
                schema: "scheduled_jobs_insights");

            migrationBuilder.DropTable(
                name: "JobExecutions",
                schema: "scheduled_jobs_insights");
        }
    }
}
