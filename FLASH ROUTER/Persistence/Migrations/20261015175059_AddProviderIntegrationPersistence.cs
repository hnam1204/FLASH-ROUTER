using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderIntegrationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderCapabilityDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Capability = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RequiredCredentialKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EndpointReference = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCapabilityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCheckRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SafeErrorCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SafeUserMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCheckRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderCredentialReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CredentialId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CredentialKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    VaultKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderCredentialReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Capability = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MetricName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ProviderReportedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderSyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextEligibleAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSuccessfulSyncAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderSyncStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCapabilityDefinitions_ProviderId_Capability",
                table: "ProviderCapabilityDefinitions",
                columns: new[] { "ProviderId", "Capability" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCheckRuns_ConnectionId_CompletedAtUtc",
                table: "ProviderCheckRuns",
                columns: new[] { "ConnectionId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCredentialReferences_ConnectionId",
                table: "ProviderCredentialReferences",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCredentialReferences_ProviderId_CredentialId",
                table: "ProviderCredentialReferences",
                columns: new[] { "ProviderId", "CredentialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderObservations_CheckRunId_MetricName",
                table: "ProviderObservations",
                columns: new[] { "CheckRunId", "MetricName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderObservations_ConnectionId_Capability_MetricName_ObservedAtUtc",
                table: "ProviderObservations",
                columns: new[] { "ConnectionId", "Capability", "MetricName", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSyncStates_ConnectionId",
                table: "ProviderSyncStates",
                column: "ConnectionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderCapabilityDefinitions");

            migrationBuilder.DropTable(
                name: "ProviderCheckRuns");

            migrationBuilder.DropTable(
                name: "ProviderCredentialReferences");

            migrationBuilder.DropTable(
                name: "ProviderObservations");

            migrationBuilder.DropTable(
                name: "ProviderSyncStates");
        }
    }
}
