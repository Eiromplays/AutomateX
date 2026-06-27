using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomateX.Migrations
{
    /// <inheritdoc />
    public partial class AddSubWorkflowLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Depth",
                table: "Executions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentExecutionId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStepOrder",
                table: "Executions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Depth",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ParentExecutionId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "ParentStepOrder",
                table: "Executions");
        }
    }
}
