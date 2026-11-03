using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CookMate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<int>(
                name: "AuthorId",
                table: "Recipes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Recipes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "Recipes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "Recipes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedById",
                table: "Recipes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Recipes",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "Recipes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_AuthorId_Status",
                table: "Recipes",
                columns: new[] { "AuthorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_ReviewedById",
                table: "Recipes",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Status_SubmittedAt",
                table: "Recipes",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Recipes_Status",
                table: "Recipes",
                sql: "[Status] BETWEEN 0 AND 2");

            migrationBuilder.AddForeignKey(
                name: "FK_Recipes_Users_AuthorId",
                table: "Recipes",
                column: "AuthorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Recipes_Users_ReviewedById",
                table: "Recipes",
                column: "ReviewedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Recipes_Users_AuthorId",
                table: "Recipes");

            migrationBuilder.DropForeignKey(
                name: "FK_Recipes_Users_ReviewedById",
                table: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_Recipes_AuthorId_Status",
                table: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_Recipes_ReviewedById",
                table: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_Recipes_Status_SubmittedAt",
                table: "Recipes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Recipes_Status",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "Recipes");
        }
    }
}
