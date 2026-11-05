using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class AttachmentProcessor(TelegramOptions options) : IAttachmentProcessor
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".cs",
        ".rb",
        ".py",
        ".cpp",
        ".h",
        ".json",
        ".yaml",
        ".pdf",
        ".docx"
    };
    public string ValidateName(string name, long size)
    {
        if (size < 0 || size > options.MaxAttachmentBytes || name != Path.GetFileName(name) || name.Contains('\\') || name.Length > 150)
            throw new ArgumentException("Invalid attachment name or size");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!Allowed.Contains(extension))
            throw new ArgumentException("Unsupported attachment extension");
        return extension;
    }

    public async Task<string> ExtractAsync(string path, string extension, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.Length > options.MaxAttachmentBytes)
            throw new InvalidDataException("Attachment exceeds configured limit");
        string text;
        if (extension == ".docx")
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (archive.Entries.Count > 300 || archive.Entries.Sum(e => e.Length) > 20 * 1024 * 1024)
                throw new InvalidDataException("DOCX exceeds archive limits");
            var entry = archive.GetEntry("word/document.xml") ?? throw new InvalidDataException("Not a DOCX document");
            using var content = entry.Open();
            using var xml = XmlReader.Create(content, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 5_000_000 });
            var doc = XDocument.Load(xml);
            text = string.Join("\n", doc.Descendants().Where(e => e.Name.LocalName == "p").Select(e => string.Concat(e.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))));
        }
        else if (extension == ".pdf")
        {
            if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
                throw new InvalidDataException("Invalid PDF header");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var start = new ProcessStartInfo("/usr/bin/pdftotext")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("-layout");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add("-");
            using var process = Process.Start(start) ?? throw new IOException("pdftotext did not start");
            try
            {
                var output = new StringBuilder();
                var buffer = new char[4096];
                while (await process.StandardOutput.ReadAsync(buffer, timeout.Token)is var read && read > 0)
                {
                    output.Append(buffer, 0, read);
                    if (output.Length > 100_000)
                        throw new InvalidDataException("PDF text exceeds limit");
                }

                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode != 0)
                    throw new InvalidDataException("PDF text extraction failed");
                text = output.ToString();
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
        }
        else
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Contains('\0') || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t' or '\uFEFF')))
                throw new InvalidDataException("Binary content rejected");
            if (extension == ".json")
                using (System.Text.Json.JsonDocument.Parse(text))
                {
                }
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("Document has no extractable text (OCR is not enabled)");
        if (text.Length > 90_000)
            throw new InvalidDataException("Extracted text exceeds 90000 characters");
        return text;
    }
}
