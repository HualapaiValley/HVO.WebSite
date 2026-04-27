using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HVO.DataModels.Migrations.V9
{
    /// <inheritdoc />
    public partial class AddApiKeyEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeyOwner",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntraObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyOwner", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiKey",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKey", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKey_ApiKeyOwner_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "v9",
                        principalTable: "ApiKeyOwner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeyClaim",
                schema: "v9",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiKeyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ClaimValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyClaim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeyClaim_ApiKey_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalSchema: "v9",
                        principalTable: "ApiKey",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKey_IsActive",
                schema: "v9",
                table: "ApiKey",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKey_KeyHash",
                schema: "v9",
                table: "ApiKey",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKey_OwnerId",
                schema: "v9",
                table: "ApiKey",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyClaim_ApiKeyId_ClaimType",
                schema: "v9",
                table: "ApiKeyClaim",
                columns: new[] { "ApiKeyId", "ClaimType" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyOwner_EntraObjectId",
                schema: "v9",
                table: "ApiKeyOwner",
                column: "EntraObjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeyClaim",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "ApiKey",
                schema: "v9");

            migrationBuilder.DropTable(
                name: "ApiKeyOwner",
                schema: "v9");
        }
    }
}
