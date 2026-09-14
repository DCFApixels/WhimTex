using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class BrushPresetLibrary
    {
        internal const string Extension = "sebrush";
        private const int Magic = 0x42525348;
        private const int Version = 1;
        private const int MaxJsonBytes = 512 * 1024;
        private const int MaxTipPixels = 16 * 1024 * 1024;
        internal static string Folder => Path.GetFullPath(Path.Combine(WhimTexUserSettings.PresetsFolder, "Brushes"));

        [Serializable]
        internal sealed class Preset
        {
            public float size = 32f, hardness = .8f, spacing = .16f;
            public BrushDynamics dynamics = new BrushDynamics();
            public int width, height;
            public bool srgb;
            public FilterMode filter;
            public TextureWrapMode wrapU, wrapV;
        }

        internal static Preset Capture(PaintToolSettings settings)
        {
            var dynamics = JsonUtility.FromJson<BrushDynamics>(JsonUtility.ToJson(settings.dynamics));
            dynamics.tip = null;
            dynamics.Normalize();
            return new Preset { size = settings.brushSize, hardness = settings.brushHardness,
                spacing = settings.brushSpacing, dynamics = dynamics };
        }

        internal static string[] List()
        {
            var paths = new HashSet<string>(Application.platform == RuntimePlatform.WindowsEditor ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string path in PresetLibraryPaths.ProjectFiles(Extension)) paths.Add(PresetLibraryPaths.PhysicalPath(path));
            foreach (string path in PresetLibraryPaths.UserFiles(Folder, Extension)) paths.Add(Path.GetFullPath(path));
            string[] files = new string[paths.Count];
            paths.CopyTo(files);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }

        internal static string MenuLabel(string path)
        {
            string label;
            if (PresetLibraryPaths.IsInside(path, Folder))
                label = "User/" + Path.GetRelativePath(Folder, path).Replace('\\', '/');
            else
            {
                string assetPath = PresetLibraryPaths.AssetPath(path);
                if (assetPath == null)
                    foreach (string candidate in PresetLibraryPaths.ProjectFiles(Extension))
                        if (string.Equals(PresetLibraryPaths.PhysicalPath(candidate), path,
                            Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        { assetPath = candidate; break; }
                label = "Project/" + (assetPath ?? Path.GetFileName(path));
            }
            return label.Substring(0, label.Length - Path.GetExtension(path).Length);
        }

        internal static void Save(string path, PaintToolSettings settings, bool overwrite)
        {
            path = ValidateDestination(path);
            if (settings.dynamics.tip == null && (!string.IsNullOrEmpty(settings.brushTipGuid) || !string.IsNullOrEmpty(settings.brushTipPresetPath)))
                throw new IOException("The brush tip is unavailable. Restore it or explicitly clear Texture before saving the preset.");
            Preset preset = Capture(settings);
            byte[] pixels = CaptureTip(settings.dynamics.tip, preset);
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(preset));
            if (json.Length > MaxJsonBytes) throw new IOException("Brush settings are too large.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal))
                using (var writer = new BinaryWriter(zip))
                {
                    writer.Write(Magic); writer.Write(Version);
                    writer.Write(json.Length); writer.Write(json);
                    writer.Write(pixels.Length); writer.Write(pixels);
                }
                if (File.Exists(path))
                {
                    if (!overwrite) throw new IOException("A preset with this name already exists.");
                    File.Replace(temporary, path, path + ".bak");
                }
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            PresetLibraryPaths.ImportSavedFile(path);
        }

        internal static string ValidateDestination(string path)
        {
            return PresetLibraryPaths.ValidateDestination(path, Folder, Extension);
        }

        internal static Preset Load(string path, out Texture2D tip)
        {
            tip = null;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new BinaryReader(zip))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
                    throw new IOException("Unsupported brush preset format or version.");
                int jsonLength = reader.ReadInt32();
                if (jsonLength < 2 || jsonLength > MaxJsonBytes) throw new IOException("Invalid brush settings length.");
                Preset preset = JsonUtility.FromJson<Preset>(Encoding.UTF8.GetString(ReadExactly(reader, jsonLength)));
                if (preset == null || preset.dynamics == null) throw new IOException("Missing brush settings.");
                preset.dynamics.tip = null;
                preset.dynamics.Normalize();
                preset.size = FiniteRange(preset.size, 1f, 4096f);
                preset.hardness = FiniteRange(preset.hardness, 0f, 1f);
                preset.spacing = FiniteRange(preset.spacing, DrawingLayerBehaviour.MinimumBrushSpacing, DrawingLayerBehaviour.MaximumBrushSpacing);
                int bytes = reader.ReadInt32();
                int expected = TipByteCount(preset.width, preset.height, preset.srgb);
                if (bytes != expected) throw new IOException("Invalid brush tip length.");
                byte[] raw = ReadExactly(reader, bytes);
                if (reader.BaseStream.ReadByte() != -1) throw new IOException("Unexpected data after the brush preset.");
                if (bytes == 0) return preset;
                if (!Enum.IsDefined(typeof(FilterMode), preset.filter) || !Enum.IsDefined(typeof(TextureWrapMode), preset.wrapU) ||
                    !Enum.IsDefined(typeof(TextureWrapMode), preset.wrapV)) throw new IOException("Invalid tip sampling settings.");
                try
                {
                    tip = new Texture2D(preset.width, preset.height, preset.srgb ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf, false, !preset.srgb)
                    {
                        name = Path.GetFileNameWithoutExtension(path) + " Tip", hideFlags = HideFlags.HideAndDontSave,
                        filterMode = preset.filter, wrapModeU = preset.wrapU, wrapModeV = preset.wrapV
                    };
                    tip.LoadRawTextureData(raw);
                    tip.Apply(false, true);
                    return preset;
                }
                catch { if (tip != null) UnityEngine.Object.DestroyImmediate(tip); tip = null; throw; }
            }
        }

        private static int TipByteCount(int width, int height, bool srgb)
        {
            if (width == 0 && height == 0) return 0;
            if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > MaxTipPixels)
                throw new IOException("Brush tips must be at most 8192 pixels per side and 16 megapixels in total.");
            return checked(width * height * (srgb ? 4 : 8));
        }

        private static byte[] ReadExactly(BinaryReader reader, int count)
        {
            byte[] data = reader.ReadBytes(count);
            if (data.Length != count) throw new EndOfStreamException("The brush preset is incomplete.");
            return data;
        }

        private static float FiniteRange(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new IOException("Nonfinite brush setting.");
            return Mathf.Clamp(value, min, max);
        }

        private static byte[] CaptureTip(Texture2D tip, Preset preset)
        {
            if (tip == null) return Array.Empty<byte>();
            preset.width = tip.width; preset.height = tip.height; preset.srgb = tip.isDataSRGB;
            TipByteCount(preset.width, preset.height, preset.srgb);
            preset.filter = tip.filterMode; preset.wrapU = tip.wrapModeU; preset.wrapV = tip.wrapModeV;
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture target = RenderTexture.GetTemporary(tip.width, tip.height, 0,
                preset.srgb ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf,
                preset.srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Texture2D copy = null;
            try
            {
                GL.sRGBWrite = preset.srgb && QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(tip, target);
                copy = new Texture2D(tip.width, tip.height, preset.srgb ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf, false, !preset.srgb)
                    { hideFlags = HideFlags.HideAndDontSave };
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, tip.width, tip.height), 0, 0, false);
                return copy.GetRawTextureData<byte>().ToArray();
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
