using System.Text;
using PhotoLibrarian.Core.Services;
using Xunit;

namespace PhotoLibrarian.Tests;

public sealed class JpegXmpPacketTests
{
    // Small structurally valid JPEG stream carrying EXIF and SOS markers. The
    // XMP inserter preserves every byte of the original JPEG outside old XMP.
    private static readonly byte[] SampleJpeg =
    [
        0xFF, 0xD8,
        0xFF, 0xE1, 0, 8, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
        0xFF, 0xDA, 0, 2,
        0x11, 0x22, 0xFF, 0xD9
    ];

    private static byte[] Packet(string content) => Encoding.UTF8.GetBytes(
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        content + "</rdf:RDF></x:xmpmeta>");

    [Fact]
    public void Embed_PreservesEntirePacketAndOriginalExifAndScanBytes()
    {
        var xmp = Packet("<rdf:Description rdf:about=\"\" custom=\"value\" />");
        var result = JpegXmpPacket.Embed(SampleJpeg, xmp);

        Assert.True(JpegXmpPacket.ContainsExactPacket(result, xmp));
        Assert.Equal(0xFF, result[0]);
        Assert.Equal(0xD8, result[1]);
        Assert.True(result.AsSpan().EndsWith(SampleJpeg.AsSpan(2)));
    }

    [Fact]
    public void Embed_ReplacesOlderEmbeddedXmpWithoutCreatingDuplicates()
    {
        var first = Packet("<rdf:Description rdf:about=\"\" old=\"yes\" />");
        var second = Packet("<rdf:Description rdf:about=\"\" new=\"yes\" />");
        var twice = JpegXmpPacket.Embed(JpegXmpPacket.Embed(SampleJpeg, first), second);

        Assert.False(JpegXmpPacket.ContainsExactPacket(twice, first));
        Assert.True(JpegXmpPacket.ContainsExactPacket(twice, second));
    }

    [Fact]
    public void RejectsOversizedMetadataAndNonJpegDataWithoutTruncation()
    {
        Assert.Throws<InvalidDataException>(() =>
            JpegXmpPacket.Embed(SampleJpeg, new byte[70_000]));
        Assert.Throws<InvalidDataException>(() =>
            JpegXmpPacket.Embed([1, 2, 3, 4], Packet("empty")));
    }
}
