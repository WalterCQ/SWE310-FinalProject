using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProviderCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SupportsToolCalls = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiProviderCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Goal = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PlanJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CurrentSubAgent = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentJobs_AiProviderCredentials_ProviderCredentialId",
                        column: x => x.ProviderCredentialId,
                        principalTable: "AiProviderCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AgentJobs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AgentJobs_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentEvents_AgentJobs_AgentJobId",
                        column: x => x.AgentJobId,
                        principalTable: "AgentJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SubAgentName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutputJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentSteps_AgentJobs_AgentJobId",
                        column: x => x.AgentJobId,
                        principalTable: "AgentJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentSubJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubAgentName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Goal = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSubJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentSubJobs_AgentJobs_AgentJobId",
                        column: x => x.AgentJobId,
                        principalTable: "AgentJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActionName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PreviewJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetEntityType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    TargetEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExecutedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecutionResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentApprovals_AgentJobs_AgentJobId",
                        column: x => x.AgentJobId,
                        principalTable: "AgentJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentApprovals_AgentSteps_AgentStepId",
                        column: x => x.AgentStepId,
                        principalTable: "AgentSteps",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AgentArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StorageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentArtifacts_AgentJobs_AgentJobId",
                        column: x => x.AgentJobId,
                        principalTable: "AgentJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentArtifacts_AgentSteps_AgentStepId",
                        column: x => x.AgentStepId,
                        principalTable: "AgentSteps",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentApprovals_AgentJobId_Status",
                table: "AgentApprovals",
                columns: new[] { "AgentJobId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentApprovals_AgentStepId",
                table: "AgentApprovals",
                column: "AgentStepId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentArtifacts_AgentJobId",
                table: "AgentArtifacts",
                column: "AgentJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentArtifacts_AgentStepId",
                table: "AgentArtifacts",
                column: "AgentStepId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentEvents_AgentJobId_CreatedAtUtc",
                table: "AgentEvents",
                columns: new[] { "AgentJobId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentJobs_ProviderCredentialId",
                table: "AgentJobs",
                column: "ProviderCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentJobs_Status_LockedAtUtc",
                table: "AgentJobs",
                columns: new[] { "Status", "LockedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentJobs_UserId",
                table: "AgentJobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentJobs_WorkspaceId",
                table: "AgentJobs",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentSteps_AgentJobId_Sequence",
                table: "AgentSteps",
                columns: new[] { "AgentJobId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentSubJobs_AgentJobId",
                table: "AgentSubJobs",
                column: "AgentJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderCredentials_UserId_IsDefault",
                table: "AiProviderCredentials",
                columns: new[] { "UserId", "IsDefault" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentApprovals");

            migrationBuilder.DropTable(
                name: "AgentArtifacts");

            migrationBuilder.DropTable(
                name: "AgentEvents");

            migrationBuilder.DropTable(
                name: "AgentSubJobs");

            migrationBuilder.DropTable(
                name: "AgentSteps");

            migrationBuilder.DropTable(
                name: "AgentJobs");

            migrationBuilder.DropTable(
                name: "AiProviderCredentials");
        }
    }
}
