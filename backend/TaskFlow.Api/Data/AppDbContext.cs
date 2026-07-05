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
    public DbSet<AiProviderCredential> AiProviderCredentials => Set<AiProviderCredential>();
    public DbSet<WorkspaceAiProviderCredential> WorkspaceAiProviderCredentials => Set<WorkspaceAiProviderCredential>();
    public DbSet<ChannelAttachment> ChannelAttachments => Set<ChannelAttachment>();
    public DbSet<ChannelAttachmentBlob> ChannelAttachmentBlobs => Set<ChannelAttachmentBlob>();
    public DbSet<ChannelKnowledgeChunk> ChannelKnowledgeChunks => Set<ChannelKnowledgeChunk>();
    public DbSet<AgentJob> AgentJobs => Set<AgentJob>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();
    public DbSet<AgentSubJob> AgentSubJobs => Set<AgentSubJob>();
    public DbSet<AgentApproval> AgentApprovals => Set<AgentApproval>();
    public DbSet<AgentArtifact> AgentArtifacts => Set<AgentArtifact>();
    public DbSet<AgentArtifactBlob> AgentArtifactBlobs => Set<AgentArtifactBlob>();
    public DbSet<AgentEvent> AgentEvents => Set<AgentEvent>();
    public DbSet<GitHubRepositoryConnection> GitHubRepositoryConnections => Set<GitHubRepositoryConnection>();

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

        modelBuilder.Entity<AiProviderCredential>(entity =>
        {
            entity.HasIndex(credential => new { credential.UserId, credential.IsDefault });
            entity.Property(credential => credential.ProviderName).HasMaxLength(80);
            entity.Property(credential => credential.BaseUrl).HasMaxLength(500);
            entity.Property(credential => credential.Model).HasMaxLength(120);
            entity.Property(credential => credential.EncryptedApiKey).HasMaxLength(4000);
            entity.HasOne(credential => credential.User)
                .WithMany()
                .HasForeignKey(credential => credential.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceAiProviderCredential>(entity =>
        {
            entity.HasIndex(credential => credential.WorkspaceId).IsUnique();
            entity.Property(credential => credential.ProviderName).HasMaxLength(80);
            entity.Property(credential => credential.BaseUrl).HasMaxLength(500);
            entity.Property(credential => credential.Model).HasMaxLength(120);
            entity.Property(credential => credential.EncryptedApiKey).HasMaxLength(4000);
            entity.HasOne(credential => credential.Workspace)
                .WithMany()
                .HasForeignKey(credential => credential.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(credential => credential.CreatedByUser)
                .WithMany()
                .HasForeignKey(credential => credential.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(credential => credential.UpdatedByUser)
                .WithMany()
                .HasForeignKey(credential => credential.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChannelAttachment>(entity =>
        {
            entity.HasIndex(attachment => new { attachment.ChannelId, attachment.CreatedAtUtc });
            entity.HasIndex(attachment => attachment.MessageId);
            entity.Property(attachment => attachment.FileName).HasMaxLength(260);
            entity.Property(attachment => attachment.ContentType).HasMaxLength(160);
            entity.Property(attachment => attachment.Summary).HasMaxLength(4000);
            entity.HasOne(attachment => attachment.Workspace)
                .WithMany()
                .HasForeignKey(attachment => attachment.WorkspaceId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(attachment => attachment.Channel)
                .WithMany()
                .HasForeignKey(attachment => attachment.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(attachment => attachment.Message)
                .WithMany(message => message.Attachments)
                .HasForeignKey(attachment => attachment.MessageId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(attachment => attachment.UploadedByUser)
                .WithMany()
                .HasForeignKey(attachment => attachment.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(attachment => attachment.Blob)
                .WithOne(blob => blob.Attachment)
                .HasForeignKey<ChannelAttachmentBlob>(blob => blob.AttachmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChannelAttachmentBlob>(entity =>
        {
            entity.HasKey(blob => blob.AttachmentId);
        });

        modelBuilder.Entity<ChannelKnowledgeChunk>(entity =>
        {
            entity.HasIndex(chunk => new { chunk.WorkspaceId, chunk.ChannelId });
            entity.HasIndex(chunk => chunk.AttachmentId);
            entity.Property(chunk => chunk.SourceType).HasMaxLength(40);
            entity.Property(chunk => chunk.SourceLabel).HasMaxLength(320);
            entity.Property(chunk => chunk.Content).HasMaxLength(2500);
            entity.HasOne(chunk => chunk.Workspace)
                .WithMany()
                .HasForeignKey(chunk => chunk.WorkspaceId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(chunk => chunk.Channel)
                .WithMany()
                .HasForeignKey(chunk => chunk.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(chunk => chunk.Attachment)
                .WithMany(attachment => attachment.KnowledgeChunks)
                .HasForeignKey(chunk => chunk.AttachmentId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AgentJob>(entity =>
        {
            entity.HasIndex(job => new { job.Status, job.LockedAtUtc });
            entity.HasIndex(job => job.GitHubRepositoryConnectionId);
            entity.Property(job => job.Goal).HasMaxLength(4000);
            entity.Property(job => job.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(job => job.CurrentSubAgent).HasMaxLength(120);
            entity.Property(job => job.ErrorMessage).HasMaxLength(2000);
            entity.Property(job => job.LockedBy).HasMaxLength(120);
            entity.Property(job => job.ArtifactTarget).HasMaxLength(40);
            entity.HasOne(job => job.Workspace)
                .WithMany()
                .HasForeignKey(job => job.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(job => job.User)
                .WithMany()
                .HasForeignKey(job => job.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(job => job.ProviderCredential)
                .WithMany()
                .HasForeignKey(job => job.ProviderCredentialId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(job => job.GitHubRepositoryConnection)
                .WithMany()
                .HasForeignKey(job => job.GitHubRepositoryConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AgentStep>(entity =>
        {
            entity.HasIndex(step => new { step.AgentJobId, step.Sequence }).IsUnique();
            entity.Property(step => step.Name).HasMaxLength(160);
            entity.Property(step => step.SubAgentName).HasMaxLength(120);
            entity.Property(step => step.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(step => step.ErrorMessage).HasMaxLength(2000);
            entity.HasOne(step => step.AgentJob)
                .WithMany(job => job.Steps)
                .HasForeignKey(step => step.AgentJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentSubJob>(entity =>
        {
            entity.Property(subJob => subJob.SubAgentName).HasMaxLength(120);
            entity.Property(subJob => subJob.Goal).HasMaxLength(2000);
            entity.Property(subJob => subJob.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(subJob => subJob.ErrorMessage).HasMaxLength(2000);
            entity.HasOne(subJob => subJob.AgentJob)
                .WithMany(job => job.SubJobs)
                .HasForeignKey(subJob => subJob.AgentJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentApproval>(entity =>
        {
            entity.HasIndex(approval => new { approval.AgentJobId, approval.Status });
            entity.Property(approval => approval.ApprovalType).HasMaxLength(80);
            entity.Property(approval => approval.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(approval => approval.Title).HasMaxLength(200);
            entity.Property(approval => approval.ActionName).HasMaxLength(120);
            entity.Property(approval => approval.TargetEntityType).HasMaxLength(80);
            entity.Property(approval => approval.DecisionNote).HasMaxLength(1000);
            entity.HasOne(approval => approval.AgentJob)
                .WithMany(job => job.Approvals)
                .HasForeignKey(approval => approval.AgentJobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(approval => approval.AgentStep)
                .WithMany()
                .HasForeignKey(approval => approval.AgentStepId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AgentArtifact>(entity =>
        {
            entity.Property(artifact => artifact.Kind).HasConversion<string>().HasMaxLength(40);
            entity.Property(artifact => artifact.Name).HasMaxLength(200);
            entity.Property(artifact => artifact.ContentType).HasMaxLength(120);
            entity.Property(artifact => artifact.StorageUrl).HasMaxLength(1000);
            entity.HasOne(artifact => artifact.AgentJob)
                .WithMany(job => job.Artifacts)
                .HasForeignKey(artifact => artifact.AgentJobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(artifact => artifact.AgentStep)
                .WithMany()
                .HasForeignKey(artifact => artifact.AgentStepId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(artifact => artifact.Blob)
                .WithOne(blob => blob.Artifact)
                .HasForeignKey<AgentArtifactBlob>(blob => blob.AgentArtifactId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentArtifactBlob>(entity =>
        {
            entity.HasKey(blob => blob.AgentArtifactId);
        });

        modelBuilder.Entity<AgentEvent>(entity =>
        {
            entity.HasIndex(agentEvent => new { agentEvent.AgentJobId, agentEvent.CreatedAtUtc });
            entity.Property(agentEvent => agentEvent.EventType).HasConversion<string>().HasMaxLength(40);
            entity.Property(agentEvent => agentEvent.Message).HasMaxLength(1000);
            entity.HasOne(agentEvent => agentEvent.AgentJob)
                .WithMany(job => job.Events)
                .HasForeignKey(agentEvent => agentEvent.AgentJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GitHubRepositoryConnection>(entity =>
        {
            entity.HasIndex(connection => new { connection.WorkspaceId, connection.FullName }).IsUnique();
            entity.HasIndex(connection => connection.InstallationId);
            entity.Property(connection => connection.Owner).HasMaxLength(120);
            entity.Property(connection => connection.Name).HasMaxLength(160);
            entity.Property(connection => connection.FullName).HasMaxLength(320);
            entity.Property(connection => connection.DefaultBranch).HasMaxLength(160);
            entity.Property(connection => connection.ValidationCommand).HasMaxLength(500);
            entity.Property(connection => connection.PermissionStatus).HasMaxLength(80);
            entity.HasOne(connection => connection.Workspace)
                .WithMany()
                .HasForeignKey(connection => connection.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(connection => connection.CreatedByUser)
                .WithMany()
                .HasForeignKey(connection => connection.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
