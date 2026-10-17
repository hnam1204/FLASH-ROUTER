using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccountPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NumericId = table.Column<int>(type: "INTEGER", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedDisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    NormalizedNotes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedProvider = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConnectionStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RecordedPlan = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    NormalizedRecordedPlan = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TrackingDateUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DataSource = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LifecycleStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ConnectionKind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DisplayLabel = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    ExternalIdentity = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastCheckedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountTags",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NormalizedValue = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTags", x => new { x.AccountId, x.Value });
                    table.ForeignKey(
                        name: "FK_AccountTags_AccountProfiles_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AccountProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountProfiles_IsArchived_TrackingDateUtc",
                table: "AccountProfiles",
                columns: new[] { "IsArchived", "TrackingDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountProfiles_NormalizedDisplayName",
                table: "AccountProfiles",
                column: "NormalizedDisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_AccountProfiles_NormalizedEmail_NormalizedProvider",
                table: "AccountProfiles",
                columns: new[] { "NormalizedEmail", "NormalizedProvider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountProfiles_NumericId",
                table: "AccountProfiles",
                column: "NumericId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountTags_NormalizedValue",
                table: "AccountTags",
                column: "NormalizedValue");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderConnections_AccountId",
                table: "ProviderConnections",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderConnections_AccountId_ProviderId_ConnectionKind_ExternalIdentity",
                table: "ProviderConnections",
                columns: new[] { "AccountId", "ProviderId", "ConnectionKind", "ExternalIdentity" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountTags");

            migrationBuilder.DropTable(
                name: "ProviderConnections");

            migrationBuilder.DropTable(
                name: "AccountProfiles");
        }
    }
}
