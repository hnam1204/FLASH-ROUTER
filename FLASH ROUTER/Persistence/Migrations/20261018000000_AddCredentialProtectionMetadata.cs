using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FLASHROUTER.Persistence.Migrations
{
    public partial class AddCredentialProtectionMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectionScheme",
                table: "ProviderCredentialReferences",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "AspNetCoreDataProtection-DPAPI-CurrentUser");

            migrationBuilder.AddColumn<int>(
                name: "ProtectionFormatVersion",
                table: "ProviderCredentialReferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectionScheme",
                table: "ProviderCredentialReferences");

            migrationBuilder.DropColumn(
                name: "ProtectionFormatVersion",
                table: "ProviderCredentialReferences");
        }
    }
}
