using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TaskFlow.Api.Data;

#nullable disable

namespace TaskFlow.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260706010000_PromoteDemoAdminUser")]
    public partial class PromoteDemoAdminUser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Users]
                SET [GlobalRole] = 'Administrator'
                WHERE LOWER([Name]) = 'admin user';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Users]
                SET [GlobalRole] = 'Member'
                WHERE LOWER([Name]) = 'admin user';
                """);
        }
    }
}
