using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthConnectionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectionChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConnectionSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RegistrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderSubject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IssuedClientId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HostIdentifier = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    GrantedScopes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AccessTokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastRefreshAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastVerifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuthRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IssuedClientId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HostIdentifier = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    ProviderSubject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TokenVaultReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccessTokenKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    RefreshTokenKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    IdTokenKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenVaultReferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionChecks_ConnectionId_ObservedAtUtc",
                table: "ConnectionChecks",
                columns: new[] { "ConnectionId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionSessions_AccountId_ProviderId_ProviderSubject_IssuedClientId",
                table: "ConnectionSessions",
                columns: new[] { "AccountId", "ProviderId", "ProviderSubject", "IssuedClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionSessions_ConnectionId",
                table: "ConnectionSessions",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthRegistrations_AccountId",
                table: "OAuthRegistrations",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthRegistrations_ProviderId_IssuedClientId",
                table: "OAuthRegistrations",
                columns: new[] { "ProviderId", "IssuedClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenVaultReferences_ConnectionId",
                table: "TokenVaultReferences",
                column: "ConnectionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectionChecks");

            migrationBuilder.DropTable(
                name: "ConnectionSessions");

            migrationBuilder.DropTable(
                name: "OAuthRegistrations");

            migrationBuilder.DropTable(
                name: "TokenVaultReferences");
        }
    }
}
