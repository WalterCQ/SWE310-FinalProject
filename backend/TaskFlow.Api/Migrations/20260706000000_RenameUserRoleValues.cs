using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    public partial class RenameUserRoleValues : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Users] SET [GlobalRole] = 'Administrator' WHERE [GlobalRole] = 'Admin';
                UPDATE [Users] SET [GlobalRole] = 'Member' WHERE [GlobalRole] = 'User';

                UPDATE [WorkspaceMembers] SET [Role] = 'Administrator' WHERE [Role] = 'Owner';
                UPDATE [WorkspaceMembers] SET [Role] = 'Manager' WHERE [Role] = 'Admin';

                UPDATE [ProjectMembers] SET [RoleInProject] = 'Administrator' WHERE [RoleInProject] = 'ProjectManager';
                UPDATE [ProjectMembers] SET [RoleInProject] = 'Manager' WHERE [RoleInProject] = 'Contributor';
                UPDATE [ProjectMembers] SET [RoleInProject] = 'Member' WHERE [RoleInProject] = 'Viewer';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [ProjectMembers] SET [RoleInProject] = 'Viewer' WHERE [RoleInProject] = 'Member';
                UPDATE [ProjectMembers] SET [RoleInProject] = 'Contributor' WHERE [RoleInProject] = 'Manager';
                UPDATE [ProjectMembers] SET [RoleInProject] = 'ProjectManager' WHERE [RoleInProject] = 'Administrator';

                UPDATE [WorkspaceMembers] SET [Role] = 'Admin' WHERE [Role] = 'Manager';
                UPDATE [WorkspaceMembers] SET [Role] = 'Owner' WHERE [Role] = 'Administrator';

                UPDATE [Users] SET [GlobalRole] = 'User' WHERE [GlobalRole] = 'Member';
                UPDATE [Users] SET [GlobalRole] = 'Admin' WHERE [GlobalRole] = 'Administrator';
                """);
        }
    }
}
