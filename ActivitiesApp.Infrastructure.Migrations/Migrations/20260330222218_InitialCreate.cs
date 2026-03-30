using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivitiesApp.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    city = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    cost = table.Column<double>(type: "double precision", nullable: false),
                    activitytime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    min_age = table.Column<int>(type: "integer", nullable: false),
                    max_age = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    place_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    rating = table.Column<double>(type: "double precision", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activities_city",
                table: "activities",
                column: "city");

            migrationBuilder.CreateIndex(
                name: "IX_activities_place_id",
                table: "activities",
                column: "place_id");

            migrationBuilder.CreateIndex(
                name: "IX_activities_updated_at",
                table: "activities",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities");
        }
    }
}
