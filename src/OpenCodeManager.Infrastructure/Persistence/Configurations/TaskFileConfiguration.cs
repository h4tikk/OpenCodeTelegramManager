using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class TaskFileConfiguration : IEntityTypeConfiguration<TaskFile>
{
    public void Configure(EntityTypeBuilder<TaskFile> builder)
    {
        builder.HasOne<AgentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FileAttachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TaskId, x.AttachmentId }).IsUnique();
    }
}
