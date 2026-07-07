using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskFlow.Api.Data;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260707000000_AddAgentContextAttachmentIds")]
    public partial class AddAgentContextAttachmentIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextAttachmentIdsJson",
                table: "AgentJobs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextAttachmentIdsJson",
                table: "AgentJobs");
        }
    }
}
