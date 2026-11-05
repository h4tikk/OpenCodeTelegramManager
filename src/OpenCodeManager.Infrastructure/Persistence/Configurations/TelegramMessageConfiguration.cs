using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCodeManager.Domain;

namespace OpenCodeManager.Infrastructure;
public sealed class TelegramMessageConfiguration : IEntityTypeConfiguration<TelegramMessage>
{
    public void Configure(EntityTypeBuilder<TelegramMessage> builder)
    {
        builder.HasOne<AgentTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TaskId, x.ChatId, x.Purpose }).IsUnique();
    }
}
