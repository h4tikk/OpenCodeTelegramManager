using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasIndex(x => x.AbsolutePath).IsUnique();
        builder.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
