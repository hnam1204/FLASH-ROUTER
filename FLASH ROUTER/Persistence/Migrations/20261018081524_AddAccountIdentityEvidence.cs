using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountIdentityEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastIdentityEvidenceSource",
                table: "AccountProfiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastIdentityReasonCode",
                table: "AccountProfiles",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastObservedProviderEmail",
                table: "AccountProfiles",
                type: "TEXT",
                maxLength: 254,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastIdentityEvidenceSource",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastIdentityReasonCode",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastObservedProviderEmail",
                table: "AccountProfiles");
        }
    }
}
