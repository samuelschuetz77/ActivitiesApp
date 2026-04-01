using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActivitiesApp.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityCreatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by_user_id",
                table: "activities",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_activities_created_by_user_id",
                table: "activities",
                column: "created_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_activities_created_by_user_id",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "activities");
        }
    }
}
