using System.Text;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Inserts a complete standard XMP packet into a JPEG APP1 segment without
/// recompressing the image. Unknown XMP namespaces are retained byte-for-byte.
/// Extended XMP (>64 KB) is deliberately rejected rather than truncated.
/// </summary>
public static class JpegXmpPacket
{
    private static readonly byte[] Header =
        Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");

    public static byte[] Embed(byte[] jpeg, byte[] xmp)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        ArgumentNullException.ThrowIfNull(xmp);
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new InvalidDataException("Source is not a JPEG file.");
        if (xmp.Length == 0 || xmp.Length + Header.Length + 2 > ushort.MaxValue)
            throw new InvalidDataException("XMP packet exceeds the standard JPEG APP1 segment limit.");

        // Remove existing standard XMP APP1 packets, preserving EXIF, ICC and
        // other APP segments. A sidecar takes precedence over older embedded XMP.
        using var output = new MemoryStream(jpeg.Length + xmp.Length + Header.Length + 4);
        output.Write(jpeg, 0, 2);
        output.WriteByte(0xFF);
        output.WriteByte(0xE1);
        int length = xmp.Length + Header.Length + 2;
        output.WriteByte((byte)(length >> 8));
        output.WriteByte((byte)length);
        output.Write(Header);
        output.Write(xmp);

        int position = 2;
        while (position < jpeg.Length)
        {
            if (jpeg[position] != 0xFF)
                throw new InvalidDataException("Malformed JPEG segment header.");
            int markerStart = position;
            while (position < jpeg.Length && jpeg[position] == 0xFF)
                position++;
            if (position == jpeg.Length)
                throw new InvalidDataException("Truncated JPEG marker.");
            byte marker = jpeg[position++];
            if (marker == 0xDA || marker == 0xD9)
            {
                // SOS contains entropy-coded data; preserve everything from it onward.
                output.Write(jpeg, markerStart, jpeg.Length - markerStart);
                break;
            }
            if (marker is 0x01 or >= 0xD0 and <= 0xD7)
            {
                output.Write(jpeg, markerStart, position - markerStart);
                continue;
            }
            if (position + 2 > jpeg.Length)
                throw new InvalidDataException("Truncated JPEG segment.");
            int size = (jpeg[position] << 8) | jpeg[position + 1];
            if (size < 2 || position + size > jpeg.Length)
                throw new InvalidDataException("Invalid JPEG segment length.");
            bool standardXmp = marker == 0xE1 &&
                size >= Header.Length + 2 &&
                jpeg.AsSpan(position + 2, Header.Length).SequenceEqual(Header);
            position += size;
            if (!standardXmp)
                output.Write(jpeg, markerStart, position - markerStart);
        }
        return output.ToArray();
    }

    public static bool ContainsExactPacket(byte[] jpeg, byte[] xmp)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            return false;
        int position = 2;
        while (position + 4 <= jpeg.Length && jpeg[position] == 0xFF)
        {
            while (position < jpeg.Length && jpeg[position] == 0xFF)
                position++;
            if (position >= jpeg.Length) return false;
            byte marker = jpeg[position++];
            if (marker is 0xDA or 0xD9) return false;
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
            if (position + 2 > jpeg.Length) return false;
            int len = (jpeg[position] << 8) | jpeg[position + 1];
            if (len < 2 || position + len > jpeg.Length) return false;
            if (marker == 0xE1 && len == Header.Length + xmp.Length + 2 &&
                jpeg.AsSpan(position + 2, Header.Length).SequenceEqual(Header) &&
                jpeg.AsSpan(position + 2 + Header.Length, xmp.Length).SequenceEqual(xmp))
                return true;
            position += len;
        }
        return false;
    }
}
