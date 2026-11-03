using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CookMate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTastes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tastes",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tastes",
                table: "Users");
        }
    }
}
