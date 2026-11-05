using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class DirectoryBookmarkConfiguration : IEntityTypeConfiguration<DirectoryBookmark>
{
    public void Configure(EntityTypeBuilder<DirectoryBookmark> builder)
    {
        builder.HasIndex(x => new { x.OwnerUserId, x.CanonicalPath }).IsUnique();
    }
}
