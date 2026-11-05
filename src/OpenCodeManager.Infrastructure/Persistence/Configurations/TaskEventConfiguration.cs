using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class TaskEventConfiguration : IEntityTypeConfiguration<TaskEvent>
{
    public void Configure(EntityTypeBuilder<TaskEvent> builder)
    {
        builder.HasOne<TaskAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AttemptId, x.OpenCodeEventId }).IsUnique();
    }
}
