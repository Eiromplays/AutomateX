using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomateX.Migrations
{
    /// <inheritdoc />
    public partial class AddForEachState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentItemIndex",
                table: "Executions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ForEachStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    ChildWorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    NextIndex = table.Column<int>(type: "integer", nullable: false),
                    CompletedCount = table.Column<int>(type: "integer", nullable: false),
                    ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AnyFailed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForEachStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForEachStates_Executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Executions_ParentExecutionId",
                table: "Executions",
                column: "ParentExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ForEachStates_ExecutionId_StepOrder",
                table: "ForEachStates",
                columns: new[] { "ExecutionId", "StepOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForEachStates");

            migrationBuilder.DropIndex(
                name: "IX_Executions_ParentExecutionId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ParentItemIndex",
                table: "Executions");
        }
    }
}
