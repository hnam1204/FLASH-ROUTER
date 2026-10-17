using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderIntegrationForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_ProviderCheckRuns_ProviderConnections_ConnectionId",
                table: "ProviderCheckRuns",
                column: "ConnectionId",
                principalTable: "ProviderConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderCredentialReferences_ProviderConnections_ConnectionId",
                table: "ProviderCredentialReferences",
                column: "ConnectionId",
                principalTable: "ProviderConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderObservations_ProviderCheckRuns_CheckRunId",
                table: "ProviderObservations",
                column: "CheckRunId",
                principalTable: "ProviderCheckRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderObservations_ProviderConnections_ConnectionId",
                table: "ProviderObservations",
                column: "ConnectionId",
                principalTable: "ProviderConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderSyncStates_ProviderConnections_ConnectionId",
                table: "ProviderSyncStates",
                column: "ConnectionId",
                principalTable: "ProviderConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProviderCheckRuns_ProviderConnections_ConnectionId",
                table: "ProviderCheckRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderCredentialReferences_ProviderConnections_ConnectionId",
                table: "ProviderCredentialReferences");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderObservations_ProviderCheckRuns_CheckRunId",
                table: "ProviderObservations");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderObservations_ProviderConnections_ConnectionId",
                table: "ProviderObservations");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderSyncStates_ProviderConnections_ConnectionId",
                table: "ProviderSyncStates");
        }
    }
}
