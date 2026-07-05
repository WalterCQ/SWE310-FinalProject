using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageAttachmentFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAiIndexed",
                table: "ChannelAttachments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "MessageId",
                table: "ChannelAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelAttachmentBlobs",
                columns: table => new
                {
                    AttachmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAttachmentBlobs", x => x.AttachmentId);
                    table.ForeignKey(
                        name: "FK_ChannelAttachmentBlobs_ChannelAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "ChannelAttachments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAttachments_MessageId",
                table: "ChannelAttachments",
                column: "MessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChannelAttachments_Messages_MessageId",
                table: "ChannelAttachments",
                column: "MessageId",
                principalTable: "Messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChannelAttachments_Messages_MessageId",
                table: "ChannelAttachments");

            migrationBuilder.DropTable(
                name: "ChannelAttachmentBlobs");

            migrationBuilder.DropIndex(
                name: "IX_ChannelAttachments_MessageId",
                table: "ChannelAttachments");

            migrationBuilder.DropColumn(
                name: "IsAiIndexed",
                table: "ChannelAttachments");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "ChannelAttachments");
        }
    }
}
