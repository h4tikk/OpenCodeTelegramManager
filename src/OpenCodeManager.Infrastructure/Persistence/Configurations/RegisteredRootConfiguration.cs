using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class RegisteredRootConfiguration : IEntityTypeConfiguration<RegisteredRoot>
{
    public void Configure(EntityTypeBuilder<RegisteredRoot> builder)
    {
        builder.HasIndex(x => x.CanonicalPath).IsUnique();
    }
}
