using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackingDateTicksUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountProfiles_IsArchived_TrackingDateUtc",
                table: "AccountProfiles");

            migrationBuilder.AddColumn<long>(
                name: "TrackingDateTicksUtc",
                table: "AccountProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_AccountProfiles_IsArchived_TrackingDateTicksUtc",
                table: "AccountProfiles",
                columns: new[] { "IsArchived", "TrackingDateTicksUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountProfiles_IsArchived_TrackingDateTicksUtc",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "TrackingDateTicksUtc",
                table: "AccountProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_AccountProfiles_IsArchived_TrackingDateUtc",
                table: "AccountProfiles",
                columns: new[] { "IsArchived", "TrackingDateUtc" });
        }
    }
}
