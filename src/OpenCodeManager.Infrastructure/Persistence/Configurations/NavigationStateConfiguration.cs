using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class NavigationStateConfiguration : IEntityTypeConfiguration<NavigationState>
{
    public void Configure(EntityTypeBuilder<NavigationState> builder)
    {
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}
