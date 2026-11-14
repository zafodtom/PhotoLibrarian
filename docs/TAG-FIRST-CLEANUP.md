# Tag-first PhotoLibrarian cleanup — decision record

User decisions (November 2026): simplify the app into an album/tag manager; keep the advanced photo editor and optional automatic tagging, retire face recognition, and extract the ModelBench development utility.

## Stage 1: implemented in PR #16

| Decision | Change | Compatibility |
|---|---|---|
| Default filename A–Z sort | XAML default index aligned with ViewModel, initialization events ignored, group/sort indices corrected | Existing user-selected sorts still available |
| Make a copy | Removed viewer ribbon button and disconnected command | Existing files and backup logic untouched |
| Straighten | Removed ribbon button and workflow entry points | Internal renderer left for editor/tests until separate dependency audit |
| Red eye | Removed ribbon button and entry points | Image pipeline left intact until referenced by no tests |
| Flags | Removed navigation tree and filters | Existing flag fields retained to avoid modifying saved metadata |
| Year/month grouping | Removed hidden group options and obsolete date navigation tree | Capture-date metadata retained |
| Rating grouping and sorting | Removed hidden options | Ratings stored in existing files are preserved |
| Rating/date/person/flag advanced filters | Removed obsolete widgets, control-handling code and navigation | Hierarchical include/exclude tags, media-type/extension filters retained |
| Missing metadata filters | Removed old refinement dropdown; untagged filtering is available through hierarchical tag tree | No data discarded |
| Legacy pointer handlers | Removed three empty zoom-compatibility event methods | Current zoom/pan controller unchanged |
| Store/MSIX | Removed Store-specific CI jobs and packaging files | Inno Setup updater and normal Windows build/tests retained |
| Face detection | Face-inference request forced off and activation disabled | Automatic tag inference remains; older face records left readable |
| ModelBench | Safe exporter and migration guide added | No source removal until verified independent repository exists |

## Stage 2: deliberately deferred until builds and data compatibility verified

- Remove remaining unused face/flag/rating services and UI binding remnants only after identifying every caller. Do not delete on-disk EXIF/XMP, stored person references or database columns as a cosmetic cleanup.
- ModelBench needs its own destination GitHub repository before removal from main source. See `docs/MODELBENCH-MIGRATION.md`.
- Main application still links the ModelBench test project; remove the reference and migrate benchmark-only tests after the destination is operational.
- PR #14 (drag/drop) and PR #15 (dialog styling, JPEG performance and README) are independent and should be merged only after their own CI checks.
- Validate startup, drag/drop, gallery filtering, optional auto-tagging, and the preserved editor on a copy of an album before the next release.

This staged approach intentionally prevents accidental loss of user photo metadata.
