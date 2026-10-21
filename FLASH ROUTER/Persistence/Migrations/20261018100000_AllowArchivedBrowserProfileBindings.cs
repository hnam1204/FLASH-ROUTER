using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using FlashAI.Foundation.Persistence;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations;

[DbContext(typeof(FlashAiDbContext))]
[Migration("20261018100000_AllowArchivedBrowserProfileBindings")]
public partial class AllowArchivedBrowserProfileBindings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BrowserProfileBindings_AccountId_ProviderId_BrowserKind",
            table: "BrowserProfileBindings");

        migrationBuilder.CreateIndex(
            name: "IX_BrowserProfileBindings_AccountId_ProviderId_BrowserKind",
            table: "BrowserProfileBindings",
            columns: new[] { "AccountId", "ProviderId", "BrowserKind" },
            unique: true,
            filter: "State <> 'Archived'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BrowserProfileBindings_AccountId_ProviderId_BrowserKind",
            table: "BrowserProfileBindings");

        migrationBuilder.CreateIndex(
            name: "IX_BrowserProfileBindings_AccountId_ProviderId_BrowserKind",
            table: "BrowserProfileBindings",
            columns: new[] { "AccountId", "ProviderId", "BrowserKind" },
            unique: true);
    }
}
