using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomateX.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultsJson",
                table: "ForEachStates");

            migrationBuilder.RenameColumn(
                name: "NextIndex",
                table: "ForEachStates",
                newName: "Concurrency");

            migrationBuilder.AddColumn<string[]>(
                name: "IpAllowlist",
                table: "ApiKeys",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "RateLimitPerMinute",
                table: "ApiKeys",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ForEachItemResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    ItemIndex = table.Column<int>(type: "integer", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    Failed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForEachItemResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForEachItemResults_Executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForEachItemResults_ExecutionId_StepOrder_ItemIndex",
                table: "ForEachItemResults",
                columns: new[] { "ExecutionId", "StepOrder", "ItemIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForEachItemResults");

            migrationBuilder.DropColumn(
                name: "IpAllowlist",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "RateLimitPerMinute",
                table: "ApiKeys");

            migrationBuilder.RenameColumn(
                name: "Concurrency",
                table: "ForEachStates",
                newName: "NextIndex");

            migrationBuilder.AddColumn<string>(
                name: "ResultsJson",
                table: "ForEachStates",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }
    }
}
