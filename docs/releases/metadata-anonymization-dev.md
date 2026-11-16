# PhotoLibrarian – metadata anonymization (development)

**Status:** Draft PR #47. Not yet a stable release.

## Commands
- Gallery context menu: **Anonymize photo metadata…**, also available for multi-selection.
- Settings → **ALBUM // ANONYMIZE METADATA**: full active album with subfolders (nested albums excluded), optional retained backups, confirmation, progress, cancellation and results.

Both commands remove privacy-bearing **EXIF, IPTC, XMP, GPS, creator/caption/date/camera/rating metadata, photo keyword tags, user flags and photo-level face annotations** from successfully processed images and the active album SQLite cache. Sidecars `photo.xmp` and `photo.jpg.xmp` are removed after a verified image write. Gallery references and metadata panels are refreshed afterward.

## Supported and excluded formats

- **JPEG, PNG, WebP:** metadata stripped from the container while preserving the encoded pixel data (JPEG Adobe CMYK transform marker and technical PNG display/animation chunks are retained where needed).
- **Orientation-bearing JPEG/PNG:** orientation is baked into the image before discarding EXIF; this does re-encode the picture.
- **Single-frame TIFF and BMP:** re-encoded via Windows Imaging Codec; pixel content is retained but compression, color profile or appearance may change.
- **RAW/DNG, HEIC/HEIF, AVIF, GIF, video, multi-frame TIFF and unreadable images:** deliberately **skipped or reported as failed**, never labelled as successfully anonymized.
- A loose `image.xmp` shared by different photos/RAW+JPEG pairs blocks anonymization of that file; manually separate such pairs first.

The complete album is **not** labelled anonymized if *any* files were skipped, failed or the user cancelled midway. The catalog's tag vocabulary, recent tag history and orphan person identities are cleared only on a completely successful full-album run. Automatic recognition/tagging is switched off to prevent metadata from being recreated.

## Data integrity and recovery

- The original photo and any XMP sidecars are backed up before the first write to `.album/privacy-backups/<run>/` by default.
- A staged clean image is verified by the Windows decoder, then atomically replaces the original. SQLite cleanup runs in a transaction. If file/DB operations fail, originals are restored from the backups wherever possible.
- If the backup checkbox is disabled, per-file rollback copies exist only temporarily and are discarded after success. Failures may retain recovery copies. Error reports are stored under `.album/privacy-reports/`.
- Cancellation stops at the next safe point. Files already successfully anonymized remain anonymized.

## Important limits

This feature removes recognized photo metadata; it is **not forensic secure erasure**. Retained and historical backups, Windows thumbnail caches, system restore copies, SQLite free pages/WAL and other apps may still contain data. Photo filenames, folder names, file-system timestamps, visible faces, identifiable objects, watermarks and text *inside image pixels* are not removed. Do not distribute the source album, `.album` or backup directories expecting them to be private. Verify an exported copy with an independent metadata viewer before sharing.

## Verification
- Unit tests for JPEG APP/COM removal, PNG EXIF/text/time removal, WebP EXIF/XMP/ICC removal and RIFF flag consistency.
- Tests that unsupported RAW and its XMP are unchanged.
- SQLite metadata/tag cleanup test and UI confirmation wiring checks.
- Practical tests recommended on multiple formats, EXIF orientations 1/3/6/8, RAW+JPEG siblings, backed-up and no-backup modes, and partial cancellation.
