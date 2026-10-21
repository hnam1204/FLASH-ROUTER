using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderAccountObservationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderAccountObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BrowserProfileBindingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BrowserContextId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    AuthenticatedShellDetected = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccountMenuDetected = table.Column<bool>(type: "INTEGER", nullable: false),
                    ObservationStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EvidenceSurface = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FailureReasonCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderAccountObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderAccountObservations_AccountProfiles_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AccountProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProviderAccountObservationFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObservationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EvidenceType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Confidence = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FailureReasonCode = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderAccountObservationFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderAccountObservationFields_ProviderAccountObservations_ObservationId",
                        column: x => x.ObservationId,
                        principalTable: "ProviderAccountObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderAccountObservationFields_ObservationId_Field",
                table: "ProviderAccountObservationFields",
                columns: new[] { "ObservationId", "Field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderAccountObservations_AccountId_ProviderId_ObservedAtUtc",
                table: "ProviderAccountObservations",
                columns: new[] { "AccountId", "ProviderId", "ObservedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderAccountObservationFields");

            migrationBuilder.DropTable(
                name: "ProviderAccountObservations");
        }
    }
}
