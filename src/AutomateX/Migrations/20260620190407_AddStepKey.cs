using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomateX.Migrations
{
    /// <inheritdoc />
    public partial class AddStepKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "WorkflowSteps",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            // (WorkflowVersionId, Order) is already unique, so step-<order+1> is unique per version.
            migrationBuilder.Sql(@"UPDATE ""WorkflowSteps"" SET ""Key"" = 'step-' || (""Order"" + 1);");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_WorkflowVersionId_Key",
                table: "WorkflowSteps",
                columns: new[] { "WorkflowVersionId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowSteps_WorkflowVersionId_Key",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "WorkflowSteps");
        }
    }
}
