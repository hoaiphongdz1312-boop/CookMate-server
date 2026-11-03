using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CookMate.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RescueNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IssueCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AdviceText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SafetyNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RescueNodes", x => x.Id);
                    table.CheckConstraint("CK_RescueNodes_QuestionOrAdvice", "([QuestionText] IS NULL AND [AdviceText] IS NOT NULL) OR ([QuestionText] IS NOT NULL AND [AdviceText] IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FaceEmbedding = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    FaceDim = table.Column<int>(type: "int", nullable: true),
                    FaceEnrolledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Difficulty = table.Column<int>(type: "int", nullable: false),
                    PrepMinutes = table.Column<int>(type: "int", nullable: false),
                    CookMinutes = table.Column<int>(type: "int", nullable: false),
                    BaseServings = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.CheckConstraint("CK_Recipes_BaseServings", "[BaseServings] >= 1");
                    table.CheckConstraint("CK_Recipes_Difficulty", "[Difficulty] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_Recipes_Minutes", "[PrepMinutes] >= 0 AND [CookMinutes] >= 0");
                    table.ForeignKey(
                        name: "FK_Recipes_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RescueOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NodeId = table.Column<int>(type: "int", nullable: false),
                    AnswerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NextNodeId = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "CookingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RecipeId = table.Column<int>(type: "int", nullable: false),
                    Servings = table.Column<int>(type: "int", nullable: false),
                    CurrentStep = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookingSessions", x => x.Id);
                    table.CheckConstraint("CK_CookingSessions_CurrentStep", "[CurrentStep] >= 1");
                    table.CheckConstraint("CK_CookingSessions_Servings", "[Servings] BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_CookingSessions_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CookingSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipeId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsOptional = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredients", x => x.Id);
                    table.CheckConstraint("CK_RecipeIngredients_Quantity", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipeId = table.Column<int>(type: "int", nullable: false),
                    StepNumber = table.Column<int>(type: "int", nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TimerSeconds = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeSteps", x => x.Id);
                    table.CheckConstraint("CK_RecipeSteps_StepNumber", "[StepNumber] >= 1");
                    table.CheckConstraint("CK_RecipeSteps_TimerSeconds", "[TimerSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_RecipeSteps_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RescueHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    IssueCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecipeId = table.Column<int>(type: "int", nullable: true),
                    Resolved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
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

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CookingSessions_RecipeId",
                table: "CookingSessions",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_CookingSessions_UserId_Status",
                table: "CookingSessions",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_RecipeId",
                table: "RecipeIngredients",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_CategoryId",
                table: "Recipes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Name",
                table: "Recipes",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeSteps_RecipeId_StepNumber",
                table: "RecipeSteps",
                columns: new[] { "RecipeId", "StepNumber" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CookingSessions");

            migrationBuilder.DropTable(
                name: "RecipeIngredients");

            migrationBuilder.DropTable(
                name: "RecipeSteps");

            migrationBuilder.DropTable(
                name: "RescueHistory");

            migrationBuilder.DropTable(
                name: "RescueOptions");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "RescueNodes");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
