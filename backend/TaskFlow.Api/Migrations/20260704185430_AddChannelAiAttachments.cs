using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelAiAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelAttachments_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelAttachments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChannelAttachments_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChannelKnowledgeChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceLabel = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(2500)", maxLength: 2500, nullable: false),
                    EmbeddingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelKnowledgeChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelKnowledgeChunks_ChannelAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "ChannelAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelKnowledgeChunks_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelKnowledgeChunks_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAttachments_ChannelId_CreatedAtUtc",
                table: "ChannelAttachments",
                columns: new[] { "ChannelId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAttachments_UploadedByUserId",
                table: "ChannelAttachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAttachments_WorkspaceId",
                table: "ChannelAttachments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelKnowledgeChunks_AttachmentId",
                table: "ChannelKnowledgeChunks",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelKnowledgeChunks_ChannelId",
                table: "ChannelKnowledgeChunks",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelKnowledgeChunks_WorkspaceId_ChannelId",
                table: "ChannelKnowledgeChunks",
                columns: new[] { "WorkspaceId", "ChannelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelKnowledgeChunks");

            migrationBuilder.DropTable(
                name: "ChannelAttachments");
        }
    }
}
