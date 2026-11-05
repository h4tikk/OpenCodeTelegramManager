using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class FileTransferConfiguration : IEntityTypeConfiguration<FileTransfer>
{
    public void Configure(EntityTypeBuilder<FileTransfer> builder)
    {
        builder.HasOne<FileAttachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.SetNull);
    }
}
