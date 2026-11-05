using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;

namespace OpenCodeManager.Infrastructure;
public static class ImageHeaderReader
{
    public static (string Mime, int? Width, int? Height) Read(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 24 && h.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ("image/png", BinaryPrimitives.ReadInt32BigEndian(h[16..20]), BinaryPrimitives.ReadInt32BigEndian(h[20..24]));
        if (h.Length >= 10 && (h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8)))
            return ("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(h[6..8]), BinaryPrimitives.ReadUInt16LittleEndian(h[8..10]));
        if (h.Length >= 30 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
        {
            if (h[12..16].SequenceEqual("VP8X"u8))
                return ("image/webp", 1 + h[24] + (h[25] << 8) + (h[26] << 16), 1 + h[27] + (h[28] << 8) + (h[29] << 16));
            if (h[12..16].SequenceEqual("VP8 "u8))
                return ("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(h[26..28]) & 0x3fff, BinaryPrimitives.ReadUInt16LittleEndian(h[28..30]) & 0x3fff);
            if (h[12..16].SequenceEqual("VP8L"u8))
            {
                int bits = BinaryPrimitives.ReadInt32LittleEndian(h[21..25]);
                return ("image/webp", 1 + (bits & 0x3fff), 1 + ((bits >> 14) & 0x3fff));
            }
        }

        if (h.Length >= 3 && h[0] == 0xff && h[1] == 0xd8)
        {
            for (int pos = 2; pos + 9 < h.Length;)
            {
                if (h[pos++] != 0xff)
                    continue;
                byte marker = h[pos++];
                if (marker is 0xd8 or 0xd9)
                    continue;
                if (pos + 2 > h.Length)
                    break;
                int length = BinaryPrimitives.ReadUInt16BigEndian(h[pos..(pos + 2)]);
                if (length < 2 || pos + length > h.Length)
                    break;
                if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
                    return ("image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(h[(pos + 5)..(pos + 7)]), BinaryPrimitives.ReadUInt16BigEndian(h[(pos + 3)..(pos + 5)]));
                pos += length;
            }

            return ("image/jpeg", null, null);
        }

        if (h.StartsWith("%PDF-"u8))
            return ("application/pdf", null, null);
        return ("application/octet-stream", null, null);
    }
}
