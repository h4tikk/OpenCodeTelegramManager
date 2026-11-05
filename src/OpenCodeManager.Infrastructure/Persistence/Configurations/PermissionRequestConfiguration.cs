using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class PermissionRequestConfiguration : IEntityTypeConfiguration<PermissionRequest>
{
    public void Configure(EntityTypeBuilder<PermissionRequest> builder)
    {
        builder.HasOne<AgentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OpenCodeSessionId, x.OpenCodeRequestId }).IsUnique();
    }
}
