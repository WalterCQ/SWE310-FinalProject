using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(256);
            entity.Property(user => user.Name).HasMaxLength(120);
            entity.Property(user => user.GlobalRole).HasConversion<string>().HasMaxLength(30);
        });

        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.Property(workspace => workspace.Name).HasMaxLength(160);
            entity.Property(workspace => workspace.Description).HasMaxLength(1000);
            entity.HasOne(workspace => workspace.CreatedByUser)
                .WithMany()
                .HasForeignKey(workspace => workspace.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkspaceMember>(entity =>
        {
            entity.HasIndex(member => new { member.WorkspaceId, member.UserId }).IsUnique();
            entity.Property(member => member.Role).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(member => member.Workspace)
                .WithMany(workspace => workspace.Members)
                .HasForeignKey(member => member.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(member => member.User)
                .WithMany(user => user.WorkspaceMemberships)
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Channel>(entity =>
        {
            entity.HasIndex(channel => new { channel.WorkspaceId, channel.Name }).IsUnique();
            entity.Property(channel => channel.Name).HasMaxLength(120);
            entity.Property(channel => channel.Description).HasMaxLength(1000);
            entity.HasOne(channel => channel.Workspace)
                .WithMany(workspace => workspace.Channels)
                .HasForeignKey(channel => channel.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(channel => channel.CreatedByUser)
                .WithMany()
                .HasForeignKey(channel => channel.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChannelMember>(entity =>
        {
            entity.HasIndex(member => new { member.ChannelId, member.UserId }).IsUnique();
            entity.HasOne(member => member.Channel)
                .WithMany(channel => channel.Members)
                .HasForeignKey(member => member.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(member => member.User)
                .WithMany(user => user.ChannelMemberships)
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.Property(message => message.Content).HasMaxLength(4000);
            entity.HasOne(message => message.Channel)
                .WithMany(channel => channel.Messages)
                .HasForeignKey(message => message.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(message => message.Sender)
                .WithMany(user => user.Messages)
                .HasForeignKey(message => message.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasIndex(project => new { project.WorkspaceId, project.Name }).IsUnique();
            entity.Property(project => project.Name).HasMaxLength(160);
            entity.Property(project => project.Description).HasMaxLength(1000);
            entity.Property(project => project.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(project => project.Workspace)
                .WithMany(workspace => workspace.Projects)
                .HasForeignKey(project => project.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(project => project.CreatedByUser)
                .WithMany()
                .HasForeignKey(project => project.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.HasIndex(member => new { member.ProjectId, member.UserId }).IsUnique();
            entity.Property(member => member.RoleInProject).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(member => member.Project)
                .WithMany(project => project.Members)
                .HasForeignKey(member => member.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(member => member.User)
                .WithMany(user => user.ProjectMemberships)
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.Property(task => task.Title).HasMaxLength(200);
            entity.Property(task => task.Description).HasMaxLength(2000);
            entity.Property(task => task.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(task => task.Priority).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(task => task.Project)
                .WithMany(project => project.Tasks)
                .HasForeignKey(task => task.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(task => task.CreatedByUser)
                .WithMany()
                .HasForeignKey(task => task.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(task => task.Assignee)
                .WithMany()
                .HasForeignKey(task => task.AssigneeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TaskComment>(entity =>
        {
            entity.Property(comment => comment.Content).HasMaxLength(2000);
            entity.HasOne(comment => comment.TaskItem)
                .WithMany(task => task.Comments)
                .HasForeignKey(comment => comment.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(comment => comment.Author)
                .WithMany()
                .HasForeignKey(comment => comment.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(notification => notification.Title).HasMaxLength(160);
            entity.Property(notification => notification.Message).HasMaxLength(1000);
            entity.Property(notification => notification.Type).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(notification => notification.User)
                .WithMany(user => user.Notifications)
                .HasForeignKey(notification => notification.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(notification => notification.Workspace)
                .WithMany(workspace => workspace.Notifications)
                .HasForeignKey(notification => notification.WorkspaceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.Property(log => log.Action).HasMaxLength(120);
            entity.Property(log => log.EntityType).HasMaxLength(80);
            entity.Property(log => log.Details).HasMaxLength(1000);
            entity.HasOne(log => log.Workspace)
                .WithMany(workspace => workspace.ActivityLogs)
                .HasForeignKey(log => log.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(log => log.User)
                .WithMany()
                .HasForeignKey(log => log.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
