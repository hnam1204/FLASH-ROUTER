using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableActivityJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SafeSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SafeDetails = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_AccountId_OccurredAtUtc",
                table: "ActivityEvents",
                columns: new[] { "AccountId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_CorrelationId",
                table: "ActivityEvents",
                column: "CorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");
        }
    }
}
