using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CookMate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Tài khoản có sẵn trước khi có xác thực email được coi là đã xác thực
            migrationBuilder.Sql("UPDATE Users SET EmailConfirmed = 1");

            migrationBuilder.CreateTable(
                name: "EmailCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailCodes_UserId_Purpose",
                table: "EmailCodes",
                columns: new[] { "UserId", "Purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailCodes");

            migrationBuilder.DropColumn(
                name: "EmailConfirmed",
                table: "Users");
        }
    }
}
