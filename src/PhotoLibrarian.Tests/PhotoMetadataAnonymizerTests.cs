using System.Buffers.Binary;
using System.Text;
using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

/// <summary>Lossless metadata-purge regression tests; no image recompression required.</summary>
public sealed class PhotoMetadataAnonymizerTests
{
    [Fact]
    public void JpegScrubDropsAppExifIptcXmpAndCommentsButPreservesScanBytes()
    {
        var input = new byte[]
        {
            0xff, 0xd8,
            0xff, 0xe1, 0, 8, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            0xff, 0xed, 0, 6, (byte)'I', (byte)'P', (byte)'T', (byte)'C',
            0xff, 0xfe, 0, 5, (byte)'a', (byte)'b', (byte)'c',
            0xff, 0xda, 0, 2, 0x0a, 0x0b, 0xff, 0x00, 0x04, 0xff, 0xd9
        };
        var result = PhotoMetadataAnonymizer.StripJpeg(input);
        Assert.Equal(new byte[]
        {
            0xff, 0xd8, 0xff, 0xda, 0, 2,
            0x0a, 0x0b, 0xff, 0x00, 0x04, 0xff, 0xd9
        }, result);
    }

    [Fact]
    public void MultiImageOrAppendedPayloadJpegsAreSkippedWithoutDataLoss()
    {
        var ordinary = new byte[]
        {
            0xff, 0xd8, 0xff, 0xda, 0, 2,
            1, 2, 3, 0xff, 0xd9
        };
        var withTrailer = ordinary.Concat("second-image"u8.ToArray()).ToArray();
        Assert.Throws<NotSupportedException>(() =>
            PhotoMetadataAnonymizer.StripJpeg(withTrailer));

        var mpf = new byte[]
        {
            0xff, 0xd8,
            0xff, 0xe2, 0, 6, (byte)'M', (byte)'P', (byte)'F', 0,
            0xff, 0xda, 0, 2, 1, 2, 0xff, 0xd9
        };
        Assert.Throws<NotSupportedException>(() =>
            PhotoMetadataAnonymizer.StripJpeg(mpf));
    }

    [Fact]
    public void PngScrubRemovesTextExifTimeAndUnknownChunksButKeepsPixels()
    {
        var bytes = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
        AddPngChunk(bytes, "IHDR", new byte[13]);
        AddPngChunk(bytes, "tEXt", "Artist=Somebody"u8.ToArray());
        AddPngChunk(bytes, "eXIf", "GPS"u8.ToArray());
        AddPngChunk(bytes, "tIME", new byte[7]);
        AddPngChunk(bytes, "IDAT", new byte[] { 1, 2, 3, 4 });
        AddPngChunk(bytes, "IEND", []);
        bytes.AddRange("trailing-secret"u8.ToArray());

        var clean = PhotoMetadataAnonymizer.StripPng(bytes.ToArray());
        Assert.True(clean.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 }));
        Assert.Contains("IHDR", Encoding.ASCII.GetString(clean));
        Assert.Contains("IDAT", Encoding.ASCII.GetString(clean));
        Assert.Contains("IEND", Encoding.ASCII.GetString(clean));
        Assert.DoesNotContain("tEXt", Encoding.ASCII.GetString(clean));
        Assert.DoesNotContain("eXIf", Encoding.ASCII.GetString(clean));
        Assert.DoesNotContain("tIME", Encoding.ASCII.GetString(clean));
        Assert.DoesNotContain("trailing-secret", Encoding.ASCII.GetString(clean));
    }

    [Fact]
    public void WebpScrubRemovesMetadataAndClearsItsFeatureFlags()
    {
        var bytes = new List<byte>("RIFF"u8.ToArray());
        bytes.AddRange(new byte[4]);
        bytes.AddRange("WEBP"u8.ToArray());
        var vp8x = new byte[10];
        vp8x[0] = 0x3c; // ICC + alpha + EXIF + XMP
        AddWebpChunk(bytes, "VP8X", vp8x);
        AddWebpChunk(bytes, "EXIF", "GPS!"u8.ToArray());
        AddWebpChunk(bytes, "XMP ", "name"u8.ToArray());
        AddWebpChunk(bytes, "ICCP", "color"u8.ToArray());
        AddWebpChunk(bytes, "VP8 ", new byte[] { 1, 2, 3, 4 });
        BinaryPrimitives.WriteUInt32LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes).Slice(4, 4),
            (uint)(bytes.Count - 8));

        var clean = PhotoMetadataAnonymizer.StripWebp(bytes.ToArray());
        var text = Encoding.ASCII.GetString(clean);
        Assert.Contains("VP8X", text);
        Assert.Contains("VP8 ", text);
        Assert.DoesNotContain("EXIF", text);
        Assert.DoesNotContain("XMP ", text);
        Assert.DoesNotContain("ICCP", text);
        Assert.Equal((byte)0x10, clean[20]); // Alpha survives; private features off.
        Assert.Equal((uint)(clean.Length - 8),
            BinaryPrimitives.ReadUInt32LittleEndian(clean.AsSpan(4, 4)));
    }

    [Fact]
    public void UnsupportedOrBrokenMediaIsNeverConsideredAnonymized()
    {
        Assert.False(PhotoMetadataAnonymizer.Supports("image.cr3"));
        Assert.False(PhotoMetadataAnonymizer.Supports("image.heic"));
        Assert.False(PhotoMetadataAnonymizer.Supports("image.gif"));
        Assert.Throws<InvalidDataException>(() =>
            PhotoMetadataAnonymizer.StripJpeg(new byte[] { 0xff, 0xd8, 1, 2 }));
        Assert.Throws<InvalidDataException>(() =>
            PhotoMetadataAnonymizer.StripPng(new byte[40]));
        Assert.Throws<InvalidDataException>(() =>
            PhotoMetadataAnonymizer.StripWebp(new byte[24]));
    }

    [Fact]
    public void AttachedAndSiblingXmpNamingVariantsAreBothRemoved()
    {
        var paths = PhotoMetadataAnonymizer.SidecarPaths(
            Path.Combine("album", "A", "image.jpg"));
        Assert.Contains(Path.Combine("album", "A", "image.xmp"), paths);
        Assert.Contains(Path.Combine("album", "A", "image.jpg.xmp"), paths);
    }

    private static void AddPngChunk(List<byte> result, string kind, byte[] data)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)data.Length);
        Encoding.ASCII.GetBytes(kind, header.AsSpan(4));
        result.AddRange(header);
        result.AddRange(data);
        result.AddRange(new byte[4]); // CRC is copied, not recalculated.
    }

    private static void AddWebpChunk(List<byte> result, string kind, byte[] data)
    {
        result.AddRange(Encoding.ASCII.GetBytes(kind));
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)data.Length);
        result.AddRange(length);
        result.AddRange(data);
        if (data.Length % 2 != 0) result.Add(0);
    }
}
