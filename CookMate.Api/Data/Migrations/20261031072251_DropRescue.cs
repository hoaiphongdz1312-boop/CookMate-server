using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CookMate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropRescue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RescueHistory");

            migrationBuilder.DropTable(
                name: "RescueOptions");

            migrationBuilder.DropTable(
                name: "RescueNodes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RescueHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipeId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    IssueCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Resolved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RescueHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RescueHistory_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RescueHistory_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RescueNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdviceText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IssueCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IssueTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    QuestionText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SafetyNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RescueNodes", x => x.Id);
                    table.CheckConstraint("CK_RescueNodes_QuestionOrAdvice", "([QuestionText] IS NULL AND [AdviceText] IS NOT NULL) OR ([QuestionText] IS NOT NULL AND [AdviceText] IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "RescueOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NextNodeId = table.Column<int>(type: "int", nullable: false),
                    NodeId = table.Column<int>(type: "int", nullable: false),
                    AnswerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RescueOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RescueOptions_RescueNodes_NextNodeId",
                        column: x => x.NextNodeId,
                        principalTable: "RescueNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RescueOptions_RescueNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "RescueNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RescueHistory_RecipeId",
                table: "RescueHistory",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RescueHistory_UserId_Resolved",
                table: "RescueHistory",
                columns: new[] { "UserId", "Resolved" });

            migrationBuilder.CreateIndex(
                name: "IX_RescueNodes_IssueCode",
                table: "RescueNodes",
                column: "IssueCode");

            migrationBuilder.CreateIndex(
                name: "IX_RescueOptions_NextNodeId",
                table: "RescueOptions",
                column: "NextNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_RescueOptions_NodeId_AnswerCode",
                table: "RescueOptions",
                columns: new[] { "NodeId", "AnswerCode" },
                unique: true);
        }
    }
}
