using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Libheif;

namespace PhotoLibrarian.Core.Services;

/// <summary>
/// Optional fallback decoder for HEIC/HEIF/AVIF when the Windows WIC codec is
/// unavailable. It is intentionally decode-only; metadata writes remain
/// handled by the existing metadata pipeline.
/// </summary>
public static class HeifFallbackDecoder
{
    private static readonly object Sync = new();
    private static bool _configured;

    public static bool IsHeifFamily(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".heif", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".avif", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<byte[]?> DecodeToJpegAsync(
        string path,
        int? maximumDimension = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsHeifFamily(path) || !File.Exists(path))
            return null;

        EnsureConfigured();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var settings = new ProcessImageSettings();
            if (maximumDimension is > 0)
            {
                settings.Width = maximumDimension.Value;
                settings.Height = maximumDimension.Value;
                settings.ResizeMode = CropScaleMode.Max;
            }

            settings.TrySetEncoderFormat(ImageMimeTypes.Jpeg);

            using var input = File.OpenRead(path);
            using var output = new MemoryStream();
            MagicImageProcessor.ProcessImage(input, output, settings);
            return output.ToArray();
        }, cancellationToken);
    }

    private static void EnsureConfigured()
    {
        if (_configured) return;

        lock (Sync)
        {
            if (_configured) return;

            CodecManager.Configure(codecs =>
            {
                codecs.UseLibheif();
            });

            _configured = true;
        }
    }
}
