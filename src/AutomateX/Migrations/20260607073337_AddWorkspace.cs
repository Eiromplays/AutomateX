using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomateX.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Connections_Name",
                table: "Connections");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Workflows",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Executions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Connections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceMembers_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_WorkspaceId",
                table: "Workflows",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Executions_WorkspaceId",
                table: "Executions",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_WorkspaceId_Name",
                table: "Connections",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMembers_WorkspaceId_Email",
                table: "WorkspaceMembers",
                columns: new[] { "WorkspaceId", "Email" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Connections_Workspaces_WorkspaceId",
                table: "Connections",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Executions_Workspaces_WorkspaceId",
                table: "Executions",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Workflows_Workspaces_WorkspaceId",
                table: "Workflows",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Connections_Workspaces_WorkspaceId",
                table: "Connections");

            migrationBuilder.DropForeignKey(
                name: "FK_Executions_Workspaces_WorkspaceId",
                table: "Executions");

            migrationBuilder.DropForeignKey(
                name: "FK_Workflows_Workspaces_WorkspaceId",
                table: "Workflows");

            migrationBuilder.DropTable(
                name: "WorkspaceMembers");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_Workflows_WorkspaceId",
                table: "Workflows");

            migrationBuilder.DropIndex(
                name: "IX_Executions_WorkspaceId",
                table: "Executions");

            migrationBuilder.DropIndex(
                name: "IX_Connections_WorkspaceId_Name",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Workflows");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Connections");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_Name",
                table: "Connections",
                column: "Name",
                unique: true);
        }
    }
}
