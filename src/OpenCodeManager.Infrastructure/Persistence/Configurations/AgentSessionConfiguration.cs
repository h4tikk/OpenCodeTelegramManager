using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class AgentSessionConfiguration : IEntityTypeConfiguration<AgentSession>
{
    public void Configure(EntityTypeBuilder<AgentSession> builder)
    {
        builder.HasIndex(x => x.OpenCodeSessionId).IsUnique();
        builder.HasIndex(x => x.ProjectId).IsUnique().HasFilter("IsMain = 1");
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentSession>().WithMany().HasForeignKey(x => x.ParentSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
