using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentArtifactsAndGitHubRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtifactTarget",
                table: "AgentJobs",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AttachmentId",
                table: "AgentJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                table: "AgentJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GitHubRepositoryConnectionId",
                table: "AgentJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "AgentArtifacts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "AgentArtifactBlobs",
                columns: table => new
                {
                    AgentArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentArtifactBlobs", x => x.AgentArtifactId);
                    table.ForeignKey(
                        name: "FK_AgentArtifactBlobs_AgentArtifacts_AgentArtifactId",
                        column: x => x.AgentArtifactId,
                        principalTable: "AgentArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitHubRepositoryConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstallationId = table.Column<long>(type: "bigint", nullable: false),
                    RepositoryId = table.Column<long>(type: "bigint", nullable: true),
                    Owner = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DefaultBranch = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ValidationCommand = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PermissionStatus = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubRepositoryConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitHubRepositoryConnections_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GitHubRepositoryConnections_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentJobs_GitHubRepositoryConnectionId",
                table: "AgentJobs",
                column: "GitHubRepositoryConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubRepositoryConnections_CreatedByUserId",
                table: "GitHubRepositoryConnections",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubRepositoryConnections_InstallationId",
                table: "GitHubRepositoryConnections",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubRepositoryConnections_WorkspaceId_FullName",
                table: "GitHubRepositoryConnections",
                columns: new[] { "WorkspaceId", "FullName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentJobs_GitHubRepositoryConnections_GitHubRepositoryConnectionId",
                table: "AgentJobs",
                column: "GitHubRepositoryConnectionId",
                principalTable: "GitHubRepositoryConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentJobs_GitHubRepositoryConnections_GitHubRepositoryConnectionId",
                table: "AgentJobs");

            migrationBuilder.DropTable(
                name: "AgentArtifactBlobs");

            migrationBuilder.DropTable(
                name: "GitHubRepositoryConnections");

            migrationBuilder.DropIndex(
                name: "IX_AgentJobs_GitHubRepositoryConnectionId",
                table: "AgentJobs");

            migrationBuilder.DropColumn(
                name: "ArtifactTarget",
                table: "AgentJobs");

            migrationBuilder.DropColumn(
                name: "AttachmentId",
                table: "AgentJobs");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "AgentJobs");

            migrationBuilder.DropColumn(
                name: "GitHubRepositoryConnectionId",
                table: "AgentJobs");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "AgentArtifacts");
        }
    }
}
