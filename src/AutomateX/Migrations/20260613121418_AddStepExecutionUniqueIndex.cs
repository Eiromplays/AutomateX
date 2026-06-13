using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomateX.Migrations
{
    /// <inheritdoc />
    public partial class AddStepExecutionUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StepExecutions_ExecutionId",
                table: "StepExecutions");

            migrationBuilder.CreateIndex(
                name: "IX_StepExecutions_ExecutionId_StepOrder",
                table: "StepExecutions",
                columns: new[] { "ExecutionId", "StepOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StepExecutions_ExecutionId_StepOrder",
                table: "StepExecutions");

            migrationBuilder.CreateIndex(
                name: "IX_StepExecutions_ExecutionId",
                table: "StepExecutions",
                column: "ExecutionId");
        }
    }
}
