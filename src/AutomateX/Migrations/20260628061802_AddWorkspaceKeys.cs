using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutomateX.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceKeys",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    WrappedDek = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceKeys", x => new { x.WorkspaceId, x.Version });
                    table.ForeignKey(
                        name: "FK_WorkspaceKeys_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceKeys_WorkspaceId_Active",
                table: "WorkspaceKeys",
                columns: new[] { "WorkspaceId", "Active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceKeys");
        }
    }
}
