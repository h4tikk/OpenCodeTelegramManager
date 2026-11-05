using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class AttachmentSelectionService(IManagerDbFactory factory, OwnerGuard guard)
{
    public async Task AddToNextTaskAsync(IEnumerable<Guid> attachments, long owner, CancellationToken ct)
    {
        guard.Demand(owner);
        await using var db = await factory.CreateAsync(ct);
        var nav = await db.Navigation.SingleAsync(x => x.UserId == owner, ct);
        var ids = JsonSerializer.Deserialize<List<Guid>>(nav.PendingFileIdsJson ?? "[]") ?? [];
        foreach (var id in attachments)
        {
            if (!await db.Attachments.AnyAsync(x => x.Id == id && x.ExpiresAtUtc > DateTime.UtcNow, ct))
                throw new FileNotFoundException();
            if (!ids.Contains(id))
                ids.Add(id);
        }

        if (ids.Count > 10)
            throw new InvalidDataException("Maximum 10 attachments per task");
        nav.PendingFileIdsJson = JsonSerializer.Serialize(ids);
        await db.SaveChangesAsync(ct);
    }
}
