using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivitiesApp.Infrastructure.Migrations.Migrations
{
    [Migration("20260407213000_AddGoogleApiDailyUsage")]
    public partial class AddGoogleApiDailyUsage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "google_api_daily_usage",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    usage_date = table.Column<DateOnly>(type: "date", nullable: false),
                    api_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_google_api_daily_usage", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_google_api_daily_usage_usage_date",
                table: "google_api_daily_usage",
                column: "usage_date");

            migrationBuilder.CreateIndex(
                name: "IX_google_api_daily_usage_usage_date_api_type",
                table: "google_api_daily_usage",
                columns: new[] { "usage_date", "api_type" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "google_api_daily_usage");
        }
    }
}
