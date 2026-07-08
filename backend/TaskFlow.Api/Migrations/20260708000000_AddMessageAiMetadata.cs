using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskFlow.Api.Data;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260708000000_AddMessageAiMetadata")]
    public partial class AddMessageAiMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiAgentJobId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiArtifactType",
                table: "Messages",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AiCreatedTaskId",
                table: "Messages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiCreatedTaskTitle",
                table: "Messages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiRequiresApproval",
                table: "Messages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AiSourcesJson",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiSuggestedTasksJson",
                table: "Messages",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAgentJobId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AiArtifactType",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AiCreatedTaskId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AiCreatedTaskTitle",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AiRequiresApproval",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AiSourcesJson",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AiSuggestedTasksJson",
                table: "Messages");
        }
    }
}
