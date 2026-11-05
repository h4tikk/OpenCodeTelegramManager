using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class FileAttachmentConfiguration : IEntityTypeConfiguration<FileAttachment>
{
    public void Configure(EntityTypeBuilder<FileAttachment> builder)
    {
        builder.HasOne<AgentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.SetNull);
    }
}
