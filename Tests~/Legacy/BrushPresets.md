# Portable brush presets

Run `node Tests~/BrushPresets.test.mjs` plus the existing brush/persistence checks.
These execute translated setting/dimension methods, reference binary framing and source
contracts, not Unity serialization, graphics readback or native filesystem replacement.
Do not trigger Unity compilation. After manual compilation, verify:

1. Save a round brush via Brushes → Save As. File is created directly in the configured
   `Brushes` subfolder, with extension `.sebrush`. Cancel saves nothing except a possibly
   created empty library folder. Saving outside that subfolder is rejected.
2. Change every brush parameter, including tint/offset/rotation, then select the preset.
   Brush fields and sample update. Palette, eraser mode, Pencil, Fill, layer symmetry,
   document pixels/dirty flag/Undo and preview scale do not change.
3. Save a non-readable imported sRGB texture tip and compare Alpha/Luminance/Color modes
   before/after loading, including partial alpha and midtones. Repeat in Gamma and Linear
   projects. Repeat with a linear data texture and HDR RGBAHalf/Float values >1.
4. Copy just the preset to another project's configured Brushes folder: tip and settings
   work without the original asset. Folder is read whenever opening the selector.
5. Edit the loaded brush: * appears, file stays untouched. Save As creates a separate file;
   Overwrite Selected requires confirmation and keeps `.bak`. Read-only folder or a corrupt,
   truncated or unsupported preset produces a dialog without replacing the current brush.
6. Recompile manually after editing a preset brush: edits survive and only the tip reloads
   from its preset file. Clear Texture/Reset Tip and recompile: tip must not resurrect.
7. Switch presets repeatedly and close/reset the window: owned textures are released, never
   imported textures. If the active preset is moved/deleted, settings persist and restoration
   reports the missing tip; saving cannot silently replace it with a round brush.

## Version 1 wire format

A single GZip stream containing little-endian BinaryWriter values: magic `0x42525348`,
version `1`, UTF-8 JSON byte length, JSON, raw tip byte length, raw tip bytes. Metadata carries
brush-only settings and tip dimensions/sRGB flag/filter/wrap modes. No project GUIDs, palette,
Unity asset paths or arbitrary JSON type names are used. sRGB tips use encoded RGBA32;
linear/data/HDR tips use linear RGBAHalf. This follows imported GPU pixels, not source-file
resolution or import settings. Mipmaps are not stored. Dimensions are bounded by 8192 per side
and 16 megapixels total, JSON by 512 KiB. No texture has width=height=0 and zero raw bytes.
Unsupported versions and malformed lengths are rejected before texture allocation. Writes
use a same-directory unique temporary file and atomic move/replace, preserving the old file
as `.bak` on overwrite. The current texture reference path is stored in EditorPrefs; its bytes
remain in the preset file rather than EditorPrefs.
