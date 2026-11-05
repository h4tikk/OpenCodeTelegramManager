using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class TaskAttachmentFactory(IManagerDbFactory factory, FileAccessPolicy access, TelegramOptions telegram, IAttachmentProcessor extractor, IOpenCodeClient api)
{
    public async Task<List<OpenCodeFileInput>> PrepareTaskFilesAsync(Guid taskId, string directory, string? provider, string? model, CancellationToken ct)
    {
        await using var db = await factory.CreateAsync(ct);
        var ids = await db.TaskFiles.Where(x => x.TaskId == taskId).Select(x => x.AttachmentId).ToListAsync(ct);
        var files = await db.Attachments.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (files.Any(x => x.MimeType.StartsWith("image/", StringComparison.Ordinal)))
        {
            var catalog = await api.GetModelsAsync(directory, ct);
            var selected = catalog.SingleOrDefault(x => x.ProviderId == provider && x.Id == model);
            if (selected?.Capabilities?.Input.Any(x => x.Contains("image", StringComparison.OrdinalIgnoreCase)) != true)
                throw new AttachmentPreparationException("У выбранной модели не заявлен image input. Выберите модель для изображений: " + string.Join(", ", catalog.Where(x => x.Enabled && x.Capabilities?.Input.Any(i => i.Contains("image", StringComparison.OrdinalIgnoreCase)) == true).Take(10).Select(x => x.ProviderId + "/" + x.Id)));
        }

        var result = new List<OpenCodeFileInput>();
        long total = 0;
        foreach (var f in files)
        {
            var path = access.StoredPath(f.LocalPath);
            total += f.Size;
            if (total > telegram.MaxAttachmentBytes * 2)
                throw new InvalidDataException("Task attachment limit exceeded");
            if (f.MimeType.StartsWith("image/", StringComparison.Ordinal) || f.MimeType == "application/pdf")
            {
                var data = await File.ReadAllBytesAsync(path, ct);
                result.Add(new("data:" + f.MimeType + ";base64," + Convert.ToBase64String(data), f.OriginalName));
            }
            else
            {
                var ext = Path.GetExtension(f.OriginalName).ToLowerInvariant();
                if (f.MimeType == "application/zip" || f.MimeType == "application/octet-stream")
                    throw new InvalidDataException("Binary/ZIP attachments can be saved or downloaded; automatic extraction for AI is disabled");
                var text = await extractor.ExtractAsync(path, ext, ct);
                result.Add(new("data:text/plain;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)), f.OriginalName));
            }
        }

        return result;
    }
}
