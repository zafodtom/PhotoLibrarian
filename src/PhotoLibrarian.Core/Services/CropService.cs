using Windows.Graphics.Imaging;
using XmpCore;
using XmpCore.Options;

namespace PhotoLibrarian.Core.Services;

public readonly record struct CropResult(
    uint Width,
    uint Height,
    uint SourceWidth,
    uint SourceHeight,
    CropRectangle Bounds);

/// <summary>Crops images in display orientation while maintaining explicit keywords and face sidecars.</summary>
public static class CropService
{
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" };

    public static bool IsSupported(string filePath) =>
        SupportedExtensions.Contains(Path.GetExtension(filePath));

    /// <summary>
    /// Replaces the existing picture. The supplied tag list comes from the album DB,
    /// since an earlier metadata write may have used an XMP fallback. Metadata and
    /// remapped face-region sidecars are committed only after the crop is encoded.
    /// </summary>
    public static async Task<CropResult> CropImageAsync(
        string filePath, BitmapBounds bounds,
        IReadOnlyCollection<string>? preservedTags = null)
    {
        if (!IsSupported(filePath))
            throw new NotSupportedException($"Crop not supported for {Path.GetExtension(filePath)}");
        if (bounds.Width == 0 || bounds.Height == 0)
            throw new ArgumentException("Crop bounds must be non-empty", nameof(bounds));

        var decoded = await WindowsImagingCodec.DecodeAsync(filePath);
        var sourceWidth = decoded.Width;
        var sourceHeight = decoded.Height;
        var crop = ClampBounds(bounds, sourceWidth, sourceHeight);
        if (crop.Width == 0 || crop.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Crop bounds do not intersect the image.");

        var sidecar = PrepareRemappedSidecar(
            filePath, sourceWidth, sourceHeight, crop, preservedTags);
        var pixels = WindowsImagingCodec.Crop(decoded.Pixels, sourceWidth, sourceHeight, crop);
        var temporaryImagePath = TemporaryImagePath(filePath);
        var stagedKeywordSidecar = Path.ChangeExtension(temporaryImagePath, ".xmp");
        string? temporaryFaceSidecar = null;
        string? temporaryExistingTagSidecar = null;
        try
        {
            var encoded = await WindowsImagingCodec.EncodeAsync(
                filePath, pixels, crop.Width, crop.Height);
            await File.WriteAllBytesAsync(temporaryImagePath, encoded);

            // Write keywords to the still-unpublished image, never to the
            // original after its backup has already been discarded.
            if (preservedTags is { Count: > 0 })
                await TagWriterService.WriteTagsToSidecarAsync(temporaryImagePath, preservedTags);

            var sidecars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sidecar is not null)
            {
                temporaryFaceSidecar = $"{sidecar.Value.Path}.{Guid.NewGuid():N}.tmp";
                await File.WriteAllTextAsync(temporaryFaceSidecar, sidecar.Value.Content);
                sidecars[sidecar.Value.Path] = temporaryFaceSidecar;
            }

            var destinationSidecar = Path.ChangeExtension(filePath, ".xmp");
            if (File.Exists(stagedKeywordSidecar))
            {
                if (sidecars.TryGetValue(destinationSidecar, out var alreadyStaged))
                {
                    // BMP/TIFF face sidecars and keyword fallbacks can share one
                    // destination: merge dc:subject into the remapped XMP packet.
                    var merged = XmpMetaFactory.ParseFromString(await File.ReadAllTextAsync(alreadyStaged));
                    SetSidecarKeywords(merged, preservedTags ?? Array.Empty<string>());
                    await File.WriteAllTextAsync(alreadyStaged,
                        XmpMetaFactory.SerializeToString(merged, new SerializeOptions()));
                }
                else
                {
                    sidecars[destinationSidecar] = stagedKeywordSidecar;
                }
            }
            else if (preservedTags is not null &&
                     !sidecars.ContainsKey(destinationSidecar) &&
                     File.Exists(destinationSidecar))
            {
                // An older metadata fallback can coexist with a newly
                // written embedded keyword list. Keep that old XMP in sync
                // rather than allowing a future scan to resurrect stale tags.
                var existingXmp = XmpMetaFactory.ParseFromString(
                    await File.ReadAllTextAsync(destinationSidecar));
                SetSidecarKeywords(existingXmp, preservedTags);
                temporaryExistingTagSidecar = $"{destinationSidecar}.{Guid.NewGuid():N}.tmp";
                await File.WriteAllTextAsync(temporaryExistingTagSidecar,
                    XmpMetaFactory.SerializeToString(existingXmp, new SerializeOptions()));
                sidecars[destinationSidecar] = temporaryExistingTagSidecar;
            }

            CommitOutputs(filePath, temporaryImagePath, sidecars);
        }
        finally
        {
            DeleteIfPresent(temporaryImagePath);
            DeleteIfPresent(stagedKeywordSidecar);
            if (temporaryFaceSidecar is not null)
                DeleteIfPresent(temporaryFaceSidecar);
            if (temporaryExistingTagSidecar is not null)
                DeleteIfPresent(temporaryExistingTagSidecar);
        }

        return new CropResult(crop.Width, crop.Height, sourceWidth, sourceHeight, crop);
    }

    /// <summary>
    /// Exports a separate, independent photo. Nonempty copiedTags are persisted
    /// to the staged image (or its XMP fallback) before any destination is touched.
    /// No face metadata is copied, because its geometry belongs to the original.
    /// </summary>
    public static async Task<(uint Width, uint Height)> ExportCropAsync(
        string filePath, string destinationPath, BitmapBounds bounds,
        IReadOnlyCollection<string>? copiedTags = null)
    {
        if (!IsSupported(filePath) || !IsSupported(destinationPath))
            throw new NotSupportedException("Crop export supports JPG, PNG, TIFF and BMP.");
        var source = Path.GetFullPath(filePath);
        var destination = Path.GetFullPath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose another filename to preserve the source.");
        if (bounds.Width == 0 || bounds.Height == 0)
            throw new ArgumentException("Crop bounds must be non-empty.", nameof(bounds));

        // A file picker confirms replacing the selected image, not its independent
        // sidecars. Refuse an ambiguous overwrite rather than retain stale tags
        // or overwrite another picture's face metadata.
        if (File.Exists(Path.ChangeExtension(destination, ".xmp")) ||
            File.Exists(destination + ".xmp"))
            throw new IOException(
                "The destination already has an XMP sidecar. Choose a unique filename to protect its metadata.");

        var decoded = await WindowsImagingCodec.DecodeAsync(source);
        var crop = ClampBounds(bounds, decoded.Width, decoded.Height);
        if (crop.Width == 0 || crop.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Crop is outside the image.");

        var pixels = WindowsImagingCodec.Crop(decoded.Pixels, decoded.Width, decoded.Height, crop);
        var data = await WindowsImagingCodec.EncodeAsync(destination, pixels, crop.Width, crop.Height);
        var temp = TemporaryImagePath(destination);
        var stagedSidecar = Path.ChangeExtension(temp, ".xmp");
        try
        {
            await File.WriteAllBytesAsync(temp, data);
            if (copiedTags is { Count: > 0 })
                await TagWriterService.WriteTagsToSidecarAsync(temp, copiedTags);

            var sidecars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(stagedSidecar))
                sidecars[Path.ChangeExtension(destination, ".xmp")] = stagedSidecar;
            CommitOutputs(destination, temp, sidecars);
        }
        finally
        {
            DeleteIfPresent(temp);
            DeleteIfPresent(stagedSidecar);
        }
        return (crop.Width, crop.Height);
    }

    private static string TemporaryImagePath(string imagePath) =>
        Path.Combine(Path.GetDirectoryName(imagePath)!,
            $"{Path.GetFileNameWithoutExtension(imagePath)}.{Guid.NewGuid():N}{Path.GetExtension(imagePath)}");

    private static RemappedSidecar? PrepareRemappedSidecar(
        string imagePath, uint sourceWidth, uint sourceHeight, CropRectangle crop,
        IReadOnlyCollection<string>? tags)
    {
        var sidecarPath = FaceMetadataStore.GetSidecarPathForImage(imagePath);
        if (!File.Exists(sidecarPath))
            return null;

        var xmp = XmpMetaFactory.ParseFromString(File.ReadAllText(sidecarPath));
        var remapped = CropMetadataRemapper.RemapMwgRegions(
            xmp, sourceWidth, sourceHeight, crop);
        if (tags is not null)
            SetSidecarKeywords(xmp, tags);
        if (!remapped && tags is null)
            return null;
        return new RemappedSidecar(
            sidecarPath,
            XmpMetaFactory.SerializeToString(xmp, new SerializeOptions()));
    }

    private static void SetSidecarKeywords(IXmpMeta xmp, IEnumerable<string> tags)
    {
        xmp.DeleteProperty(DcNamespace, "dc:subject");
        foreach (var tag in tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
                     .Select(tag => tag.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            xmp.AppendArrayItem(DcNamespace, "dc:subject",
                new PropertyOptions { IsArray = true }, tag, null);
    }

    private static void CommitOutputs(
        string imagePath,
        string temporaryImagePath,
        IReadOnlyDictionary<string, string> sidecars)
    {
        var outputs = new List<(string Target, string Staged)>
        {
            (imagePath, temporaryImagePath)
        };
        outputs.AddRange(sidecars.Select(item => (item.Key, item.Value)));

        var backups = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var installed = new List<string>();
        try
        {
            foreach (var output in outputs)
            {
                if (File.Exists(output.Target))
                {
                    var backup = $"{output.Target}.{Guid.NewGuid():N}.rollback";
                    File.Copy(output.Target, backup);
                    backups[output.Target] = backup;
                }
                else
                {
                    backups[output.Target] = null;
                }
            }
            foreach (var output in outputs)
            {
                File.Move(output.Staged, output.Target, overwrite: true);
                installed.Add(output.Target);
            }
        }
        catch
        {
            foreach (var target in installed.AsEnumerable().Reverse())
            {
                if (backups.TryGetValue(target, out var backup) && backup is not null)
                    File.Copy(backup, target, overwrite: true);
                else
                    DeleteIfPresent(target);
            }
            throw;
        }
        finally
        {
            foreach (var backup in backups.Values.Where(path => path is not null))
                DeleteIfPresent(backup!);
        }
    }

    private static CropRectangle ClampBounds(BitmapBounds bounds, uint maxWidth, uint maxHeight)
    {
        var x = Math.Min(bounds.X, maxWidth);
        var y = Math.Min(bounds.Y, maxHeight);
        return new CropRectangle(x, y, Math.Min(bounds.Width, maxWidth - x), Math.Min(bounds.Height, maxHeight - y));
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private readonly record struct RemappedSidecar(string Path, string Content);
}
