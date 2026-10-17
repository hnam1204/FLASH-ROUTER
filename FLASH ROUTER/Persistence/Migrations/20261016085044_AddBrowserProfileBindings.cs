using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrowserProfileBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrowserProfileBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BrowserKind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OpaqueProfileIdentifier = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ProfileDirectoryReference = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastOpenedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrowserProfileBindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrowserProfileBindings_AccountId_ProviderId_BrowserKind",
                table: "BrowserProfileBindings",
                columns: new[] { "AccountId", "ProviderId", "BrowserKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrowserProfileBindings_OpaqueProfileIdentifier",
                table: "BrowserProfileBindings",
                column: "OpaqueProfileIdentifier",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrowserProfileBindings");
        }
    }
}
