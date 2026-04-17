using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivitiesApp.Infrastructure.Migrations.Migrations
{
    public partial class AddCreatorInfoAndUserSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by_display_name",
                table: "activities",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_profile_picture_url",
                table: "activities",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    profile_picture_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.user_id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "user_settings");

            migrationBuilder.DropColumn(
                name: "created_by_display_name",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "created_by_profile_picture_url",
                table: "activities");
        }
    }
}
