using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomateX.Migrations
{
    /// <inheritdoc />
    public partial class AddVariablesAndEnvironments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveEnvironmentId",
                table: "Workspaces",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Variables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Secret = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Variables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Variables_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceEnvironments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceEnvironments_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VariableValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VariableId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariableValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VariableValues_Variables_VariableId",
                        column: x => x.VariableId,
                        principalTable: "Variables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VariableValues_WorkspaceEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "WorkspaceEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Variables_WorkspaceId_WorkflowId_Name",
                table: "Variables",
                columns: new[] { "WorkspaceId", "WorkflowId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VariableValues_EnvironmentId",
                table: "VariableValues",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VariableValues_VariableId_EnvironmentId",
                table: "VariableValues",
                columns: new[] { "VariableId", "EnvironmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceEnvironments_WorkspaceId_Name",
                table: "WorkspaceEnvironments",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VariableValues");

            migrationBuilder.DropTable(
                name: "Variables");

            migrationBuilder.DropTable(
                name: "WorkspaceEnvironments");

            migrationBuilder.DropColumn(
                name: "ActiveEnvironmentId",
                table: "Workspaces");
        }
    }
}
