using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostLoginAuthenticationAndActivityProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AttemptId",
                table: "ActivityEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "ActivityEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "ActivityEvents",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "ActivityEvents",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderId",
                table: "ActivityEvents",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ActivityEvents",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthenticationState",
                table: "AccountProfiles",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AddColumn<string>(
                name: "IdentityVerificationState",
                table: "AccountProfiles",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "NotChecked");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAuthenticatedSessionConfirmedAtUtc",
                table: "AccountProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAuthenticationAttemptAtUtc",
                table: "AccountProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastAuthenticationAttemptId",
                table: "AccountProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastBrowserSessionObservedAtUtc",
                table: "AccountProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastIdentityVerifiedAtUtc",
                table: "AccountProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastObservationAtUtc",
                table: "AccountProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastObservationErrorCode",
                table: "AccountProfiles",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastObservationEvidenceSource",
                table: "AccountProfiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_AccountId_AttemptId_OccurredAtUtc",
                table: "ActivityEvents",
                columns: new[] { "AccountId", "AttemptId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityEvents_AccountId_AttemptId_OccurredAtUtc",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "AttemptId",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "AuthenticationState",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "IdentityVerificationState",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastAuthenticatedSessionConfirmedAtUtc",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastAuthenticationAttemptAtUtc",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastAuthenticationAttemptId",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastBrowserSessionObservedAtUtc",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastIdentityVerifiedAtUtc",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastObservationAtUtc",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastObservationErrorCode",
                table: "AccountProfiles");

            migrationBuilder.DropColumn(
                name: "LastObservationEvidenceSource",
                table: "AccountProfiles");
        }
    }
}
