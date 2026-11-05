using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> builder)
    {
        builder.HasOne<AgentSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DeduplicationKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => x.LockKey).IsUnique().HasFilter("Status IN (1, 2, 3, 7)");
    }
}
