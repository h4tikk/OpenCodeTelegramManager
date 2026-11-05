using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class TaskAttemptConfiguration : IEntityTypeConfiguration<TaskAttempt>
{
    public void Configure(EntityTypeBuilder<TaskAttempt> builder)
    {
        builder.HasOne<AgentTask>().WithMany(x => x.Attempts).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TaskId, x.AttemptNumber }).IsUnique();
        builder.HasIndex(x => x.OpenCodeMessageId).IsUnique();
    }
}
