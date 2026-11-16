using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Removes image-level privacy metadata without decoding JPEG, PNG or WebP pixels.
/// For orientation-bearing JPEG/PNG and single-frame TIFF/BMP the image is re-encoded
/// with its visual orientation baked in. Unsupported containers must be reported,
/// never silently classified as anonymized.
/// </summary>
public static class PhotoMetadataAnonymizer
{
    private static readonly HashSet<string> Supported =
        new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff" };

    public static bool Supports(string path) => Supported.Contains(Path.GetExtension(path));

    public static async Task<byte[]> PrepareCleanImageAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (!Supports(filePath))
            throw new NotSupportedException(
                $"Privacy cleanup does not safely support {extension} images. The original was not changed.");

        cancellationToken.ThrowIfCancellationRequested();

        // Removing orientation metadata without rotating the pixels would visibly
        // turn many phone photographs sideways. Bake it into the pixels first.
        var orientation = extension is ".jpg" or ".jpeg" or ".png" or ".webp"
            ? new MetadataReaderService().ReadMetadata(filePath).Orientation : 1;
        if (extension == ".webp" && orientation is > 1 and <= 8)
            throw new NotSupportedException(
                "WebP has embedded orientation data which cannot be safely baked in.");

        var needsOrientationBake = extension is ".jpg" or ".jpeg" or ".png" &&
            orientation is > 1 and <= 8;
        if (needsOrientationBake)
        {
            // Validate multi-picture/trailer markers BEFORE decoding: a fresh
            // WIC encode could otherwise discard additional JPEG frames
            // without giving the metadata stripper a chance to reject them.
            var original = await File.ReadAllBytesAsync(filePath, cancellationToken);
            if (extension is ".jpg" or ".jpeg")
                _ = StripJpeg(original);
            else
            {
                _ = StripPng(original);
                if (HasApngAnimation(original))
                    throw new NotSupportedException(
                        "Animated PNG with orientation cannot be re-encoded safely.");
            }
        }
        byte[] bytes;
        if (extension is ".tif" or ".tiff" or ".bmp" || needsOrientationBake)
        {
            await using var stream = File.OpenRead(filePath);
            using var random = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(random).AsTask(cancellationToken);
            if (decoder.FrameCount != 1)
                throw new NotSupportedException(
                    "Multi-frame image cannot be anonymized safely without losing frames.");

            var decoded = await WindowsImagingCodec.DecodeAsync(
                filePath, cancellationToken: cancellationToken);
            bytes = await WindowsImagingCodec.EncodeAsync(
                filePath, decoded.Pixels, decoded.Width, decoded.Height, cancellationToken);
        }
        else
        {
            bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }

        return extension switch
        {
            ".jpg" or ".jpeg" => StripJpeg(bytes),
            ".png" => StripPng(bytes),
            ".webp" => StripWebp(bytes),
            // WIC creates a new TIFF/BMP container containing only pixels.
            ".tif" or ".tiff" or ".bmp" => bytes,
            _ => throw new NotSupportedException(extension)
        };
    }

    /// <summary>
    /// Removes all JPEG application segments (EXIF, XMP, IPTC, comments, ICC)
    /// without changing scan data. Keep only the narrowly defined Adobe color
    /// transform marker, needed to decode some CMYK JPEG files correctly.
    /// Removes trailing bytes after EOI as well.
    /// </summary>
    public static byte[] StripJpeg(ReadOnlySpan<byte> input)
    {
        if (input.Length < 6 || input[0] != 0xff || input[1] != 0xd8)
            throw new InvalidDataException("Invalid JPEG SOI.");
        using var output = new MemoryStream(input.Length);
        output.Write(input[..2]);
        var position = 2;
        var scan = false;
        var sawEoi = false;
        var sawScan = false;
        while (position < input.Length)
        {
            if (scan)
            {
                var start = position;
                var foundMarker = false;
                while (position + 1 < input.Length)
                {
                    if (input[position] != 0xff) { position++; continue; }
                    var next = position + 1;
                    while (next < input.Length && input[next] == 0xff) next++;
                    if (next == input.Length) break;
                    var code = input[next];
                    if (code == 0x00 || code is >= 0xd0 and <= 0xd7)
                    {
                        position = next + 1;
                        continue;
                    }
                    output.Write(input.Slice(start, position - start));
                    foundMarker = true;
                    scan = false;
                    break;
                }
                if (!foundMarker)
                    throw new InvalidDataException("JPEG entropy data has no closing marker.");
                continue;
            }
            if (input[position] != 0xff)
                throw new InvalidDataException("JPEG segment marker expected.");
            var startMarker = position;
            while (position < input.Length && input[position] == 0xff) position++;
            if (position >= input.Length)
                throw new InvalidDataException("Truncated JPEG marker.");
            var marker = input[position++];
            if (marker == 0xd9)
            {
                output.Write([ (byte)0xff, (byte)0xd9 ]);
                sawEoi = true;
                break;
            }
            if (marker is 0xd8 or 0x00)
                throw new InvalidDataException("Unexpected JPEG marker.");
            if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7)
            {
                output.Write(input.Slice(startMarker, position - startMarker));
                continue;
            }
            if (position + 2 > input.Length)
                throw new InvalidDataException("Truncated JPEG segment length.");
            var length = BinaryPrimitives.ReadUInt16BigEndian(input.Slice(position, 2));
            if (length < 2 || length > input.Length - position)
                throw new InvalidDataException("Invalid JPEG segment length.");
            var end = position + length;
            // Multi-picture format (MPO) stores frame offsets and additional
            // images outside the first JPEG stream. Stripping APP2 or the
            // trailer would destroy those images, so leave it untouched.
            if (marker == 0xe2 && length >= 6 &&
                input.Slice(position + 2, 4).SequenceEqual("MPF\0"u8))
                throw new NotSupportedException(
                    "JPEG contains MPF multiple-image metadata. It was not changed.");
            var isPrivate = marker is >= 0xe0 and <= 0xef or 0xfe;
            // Standard Adobe APP14 is exactly 12 payload bytes. Refuse
            // longer lookalikes that may carry private vendor extensions.
            var keepAdobeTransform = marker == 0xee && length == 14 &&
                input.Slice(position + 2, 5).SequenceEqual("Adobe"u8);
            if (!isPrivate || keepAdobeTransform)
                output.Write(input.Slice(startMarker, end - startMarker));
            if (marker == 0xda)
            {
                scan = true;
                sawScan = true;
            }
            position = end;
        }
        if (!sawEoi || !sawScan)
            throw new InvalidDataException("JPEG SOS/EOI marker missing.");
        // Camera software (including some older digital cameras) can append
        // proprietary metadata after the JPEG End-Of-Image marker. It is not
        // part of the encoded picture and may contain identifying information.
        // Drop it, preserving the JPEG scan data byte-for-byte. Do NOT discard
        // a recognizable second picture, Live Photo video or huge unknown
        // payload: those are separate media and need dedicated handling.
        var trailer = input[position..];
        var unsafeReason = GetUnsafeJpegTrailerReason(trailer);
        if (unsafeReason is not null)
            throw new NotSupportedException(
                $"JPEG trailer: {unsafeReason}. The original photo was not changed.");
        return output.ToArray();
    }

    /// <summary>
    /// Confirm an actual media container, rather than matching four bytes
    /// anywhere in vendor-specific metadata. Report a specific reason for
    /// files that cannot be safely flattened into a single JPEG.
    /// </summary>
    private static string? GetUnsafeJpegTrailerReason(ReadOnlySpan<byte> trailer)
    {
        const int MaxMetadataTrailerBytes = 256 * 1024;
        // Scan a bounded prefix for recognisable secondary containers. Larger
        // unknown trailers remain unsupported, irrespective of marker matches.
        var maxScan = Math.Min(trailer.Length, MaxMetadataTrailerBytes);
        for (var index = 0; index < maxScan; index++)
        {
            var remaining = trailer[index..];
            if (LooksLikeEmbeddedJpeg(remaining))
                return "an additional JPEG picture was detected";
            if (LooksLikeEmbeddedPng(remaining))
                return "an additional PNG picture was detected";
            if (LooksLikeEmbeddedRiffMedia(remaining, out var riffType))
                return $"an additional {riffType} container was detected";
            if (LooksLikeEmbeddedIsoMedia(remaining))
                return "an additional MP4/MOV/HEIF-style media container was detected";
        }
        if (trailer.Length > MaxMetadataTrailerBytes)
            return $"unknown appended data ({trailer.Length:N0} bytes) exceeds the " +
                   $"safe metadata limit ({MaxMetadataTrailerBytes:N0} bytes)";

        return null;
    }

    private static bool LooksLikeEmbeddedJpeg(ReadOnlySpan<byte> data)
    {
        // SOI followed by a syntactically valid first marker and segment.
        // A stray FF D8 in vendor metadata is NOT enough to prove a second
        // image. Require both a plausible header and its closing EOI.
        if (data.Length < 9 || data[0] != 0xff || data[1] != 0xd8 ||
            data[2] != 0xff || data[3] is 0x00 or 0xff or 0xd8 or 0xd9)
            return false;
        var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));
        if (length < 2 || length > data.Length - 4) return false;
        return data[(4 + length)..].IndexOf(new byte[] { 0xff, 0xd9 }) >= 0;
    }

    private static bool LooksLikeEmbeddedPng(ReadOnlySpan<byte> data) =>
        data.Length >= 33 &&
        data[..8].SequenceEqual(PngSignature) &&
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4)) == 13 &&
        data.Slice(12, 4).SequenceEqual("IHDR"u8);

    private static bool LooksLikeEmbeddedRiffMedia(
        ReadOnlySpan<byte> data, out string mediaType)
    {
        mediaType = string.Empty;
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8))
            return false;
        var bytes = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4));
        if (bytes < 4) return false;
        if (data.Slice(8, 4).SequenceEqual("WEBP"u8)) mediaType = "WebP";
        else if (data.Slice(8, 4).SequenceEqual("AVI "u8)) mediaType = "AVI";
        else return false;
        // Even a truncated payload with a valid RIFF header must be treated
        // as an embedded/auxiliary media object rather than stripped blindly.
        return true;
    }

    private static bool LooksLikeEmbeddedIsoMedia(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data.Slice(4, 4).SequenceEqual("ftyp"u8))
            return false;
        var boxSize = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
        // Real ftyp boxes are tiny (typically 16–32 bytes). The four ASCII
        // bytes preceding the word 'ftyp' in free-form text otherwise decode
        // to a huge length and can falsely trigger media detection.
        const uint MaxPlausibleFtypBoxSize = 1024 * 1024;
        if (boxSize < 16 || boxSize > MaxPlausibleFtypBoxSize) return false;
        var brand = data.Slice(8, 4);
        foreach (var c in brand)
        {
            if (c is not (>= (byte)'0' and <= (byte)'9' or
                          >= (byte)'A' and <= (byte)'Z' or
                          >= (byte)'a' and <= (byte)'z' or (byte)' '))
                return false;
        }
        var recognizedBrand = brand.SequenceEqual("isom"u8) ||
            brand.SequenceEqual("iso2"u8) ||
            brand.SequenceEqual("mp41"u8) ||
            brand.SequenceEqual("mp42"u8) ||
            brand.SequenceEqual("qt  "u8) ||
            brand.SequenceEqual("M4V "u8) ||
            brand.SequenceEqual("heic"u8) ||
            brand.SequenceEqual("heix"u8) ||
            brand.SequenceEqual("mif1"u8) ||
            brand.SequenceEqual("msf1"u8) ||
            brand.SequenceEqual("avif"u8) ||
            brand.SequenceEqual("avis"u8);
        // Incomplete known media headers are unsafe too. Unknown brands
        // must at least have a complete, plausibly sized ftyp box.
        return recognizedBrand || boxSize <= data.Length;
    }

    private static bool HasApngAnimation(ReadOnlySpan<byte> input)
    {
        if (input.Length < 8 || !input[..8].SequenceEqual(PngSignature))
            return false;
        var offset = 8;
        while (offset + 12 <= input.Length)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(offset, 4));
            if (count > (uint)(input.Length - offset - 12))
                throw new InvalidDataException("Invalid PNG animation chunk length.");
            if (input.Slice(offset + 4, 4).SequenceEqual("acTL"u8))
                return true;
            offset += checked((int)count + 12);
        }
        return false;
    }

    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Keep only image-critical chunks, transparency, colorspace essentials and
    /// APNG animation frames. All EXIF, XMP, textual, time and unknown ancillary
    /// metadata is discarded without changing compressed pixel content.
    /// </summary>
    public static byte[] StripPng(ReadOnlySpan<byte> input)
    {
        if (input.Length < 20 || !input[..8].SequenceEqual(PngSignature))
            throw new InvalidDataException("Invalid PNG signature.");
        using var output = new MemoryStream(input.Length);
        output.Write(input[..8]);
        int offset = 8;
        bool iend = false;
        bool idat = false;
        while (offset + 12 <= input.Length)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(input.Slice(offset, 4));
            if (count > (uint)(input.Length - offset - 12))
                throw new InvalidDataException("Invalid PNG chunk length.");
            var name = Encoding.ASCII.GetString(input.Slice(offset + 4, 4));
            var total = checked((int)count + 12);
            var critical = (input[offset + 4] & 0x20) == 0;
            var keep = critical || name is "tRNS" or "acTL" or "fcTL" or "fdAT"
                or "gAMA" or "cHRM" or "sRGB" or "sBIT";
            if (keep) output.Write(input.Slice(offset, total));
            if (name == "IDAT") idat = true;
            offset += total;
            if (name != "IEND") continue;
            iend = true;
            break;
        }
        if (!iend || !idat)
            throw new InvalidDataException("PNG has no IDAT/IEND.");
        return output.ToArray();
    }

    /// <summary>
    /// Removes WebP EXIF, XMP, ICC and any unknown ancillary RIFF chunks.
    /// Keeps lossy/lossless and animation payloads intact, fixes VP8X flags
    /// and RIFF length.
    /// </summary>
    public static byte[] StripWebp(ReadOnlySpan<byte> input)
    {
        if (input.Length < 20 || !input[..4].SequenceEqual("RIFF"u8) ||
            !input.Slice(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Invalid WebP container.");
        var declared = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(4, 4));
        if (declared < 12 || (ulong)declared + 8 > (ulong)input.Length)
            throw new InvalidDataException("Invalid WebP RIFF length.");
        var limit = checked((int)declared + 8);
        using var output = new MemoryStream(input.Length);
        output.Write(input[..12]);
        int offset = 12;
        var hasPixels = false;
        while (offset + 8 <= limit)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(offset + 4, 4));
            var padded = (ulong)size + (size & 1u);
            if (padded > (ulong)(limit - offset - 8))
                throw new InvalidDataException("Invalid WebP chunk.");
            var name = Encoding.ASCII.GetString(input.Slice(offset, 4));
            var count = checked((int)(8 + padded));
            if (name is "VP8 " or "VP8L" or "ANMF") hasPixels = true;
            if (name is "VP8X" or "VP8 " or "VP8L" or "ALPH" or "ANIM" or "ANMF")
            {
                if (name == "VP8X")
                {
                    if (size < 10)
                        throw new InvalidDataException("Incomplete VP8X flags.");
                    var chunk = input.Slice(offset, count).ToArray();
                    chunk[8] &= unchecked((byte)~(0x20 | 0x08 | 0x04));
                    output.Write(chunk);
                }
                else output.Write(input.Slice(offset, count));
            }
            offset += count;
        }
        if (!hasPixels || offset != limit)
            throw new InvalidDataException("WebP image payload missing or truncated.");
        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4),
            checked((uint)(bytes.Length - 8)));
        return bytes;
    }

    public static IReadOnlyList<string> SidecarPaths(string photoPath) =>
        new[] { Path.ChangeExtension(photoPath, ".xmp"), photoPath + ".xmp" }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
