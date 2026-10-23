// Test-only support, appended after the case. No top-level using directives.
namespace WhimTex.Tests.UnityD
{
    public sealed class MigrationD : System.IDisposable
    {
        readonly UnityEngine.RenderTexture active = UnityEngine.RenderTexture.active;
        readonly bool srgb = UnityEngine.GL.sRGBWrite;
        readonly UnityEditor.EditorWindow focus = UnityEditor.EditorWindow.focusedWindow;
        readonly UnityEngine.Object[] selection = UnityEditor.Selection.objects;
        readonly string clipboard = UnityEditor.EditorGUIUtility.systemCopyBuffer;
        readonly System.Collections.Generic.List<string> folders = new System.Collections.Generic.List<string>();
        readonly string[] preferenceKeys = {
            "DCFApixels.WhimTex.Canvas.Tool", "DCFApixels.WhimTex.Canvas.TransformReturnTool",
            "DCFApixels.WhimTex.Canvas.PaintToolSettings", "DCFApixels.WhimTex.Canvas.ToolSettings",
            "DCFApixels.WhimTex.CommentAuthor", "DCFApixels.WhimTex.CommentAuthorColor", "DCFApixels.WhimTex.CommentAuthorId"
        };
        readonly bool[] preferenceExisted = new bool[7];
        readonly string[] preferenceValues = new string[7];
        bool disposed;
        System.Exception disposalFailure;
        public MigrationD()
        {
            // Constructed before the body by AsyncD. It cannot infer ownership of windows:
            // direct window cases must finish their exact owned sessions in their own finally.
            for (int i = 0; i < preferenceKeys.Length; i++)
            {
                preferenceExisted[i] = UnityEditor.EditorPrefs.HasKey(preferenceKeys[i]);
                preferenceValues[i] = UnityEditor.EditorPrefs.GetString(preferenceKeys[i]);
            }
        }
        public static string ProjectPath(string relative)
            => System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath), relative));
        public string AssetFolder()
        {
            const string parent = "Assets/WhimTexTestMigration";
            if (!UnityEditor.AssetDatabase.IsValidFolder(parent))
                UnityEditor.AssetDatabase.CreateFolder("Assets", "WhimTexTestMigration");
            string id = System.Guid.NewGuid().ToString("N"), path = parent + "/" + id;
            if (System.IO.Directory.Exists(ProjectPath(path))) throw new System.IO.IOException("Fixture already exists: " + path);
            folders.Add(path);
            UnityEditor.AssetDatabase.CreateFolder(parent, id);
            if (!UnityEditor.AssetDatabase.IsValidFolder(path)) throw new System.IO.IOException("Cannot create fixture: " + path);
            return path;
        }
        public string TempFolder()
        {
            string path = ProjectPath("Temp/WhimTex/TestMigration/" + System.Guid.NewGuid().ToString("N"));
            if (System.IO.Directory.Exists(path)) throw new System.IO.IOException("Fixture already exists: " + path);
            folders.Add(path); System.IO.Directory.CreateDirectory(path); return path;
        }
        public static void DeleteAsset(string path)
        {
            string root = ProjectPath("Assets/WhimTexTestMigration") + System.IO.Path.DirectorySeparatorChar;
            string full = ProjectPath(path);
            if (!full.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
                throw new System.IO.IOException("Asset cleanup outside owned fixture: " + path);
            string relative = full.Substring(root.Length);
            string owner = relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)[0];
            if (!System.Guid.TryParseExact(owner, "N", out _))
                throw new System.IO.IOException("Asset cleanup requires an owned GUID folder: " + path);
            if ((System.IO.File.Exists(full) || System.IO.Directory.Exists(full)) &&
                !UnityEditor.AssetDatabase.DeleteAsset(path))
                throw new System.IO.IOException("Asset cleanup failed: " + path);
        }
        public void Dispose()
        {
            if (disposed)
            {
                if (disposalFailure != null) throw disposalFailure;
                return;
            }
            disposed = true;
            var errors = new System.Collections.Generic.List<System.Exception>();
            void Attempt(System.Action action)
            {
                try { action(); } catch (System.Exception error) { errors.Add(error); }
            }
            for (int i = folders.Count - 1; i >= 0; i--)
            {
                string path = folders[i];
                Attempt(() =>
                {
                    if (path.StartsWith("Assets/", System.StringComparison.Ordinal)) DeleteAsset(path);
                    else
                    {
                        string root = ProjectPath("Temp/WhimTex/TestMigration") + System.IO.Path.DirectorySeparatorChar;
                        string full = System.IO.Path.GetFullPath(path);
                        if (!full.StartsWith(root, System.StringComparison.OrdinalIgnoreCase) ||
                            !System.Guid.TryParseExact(full.Substring(root.Length), "N", out _) ||
                            !string.Equals(full, path, System.StringComparison.OrdinalIgnoreCase))
                            throw new System.IO.IOException("Temp cleanup outside owned project GUID folder: " + path);
                        if (System.IO.Directory.Exists(full)) System.IO.Directory.Delete(full, true);
                    }
                });
            }
            Attempt(() => UnityEngine.RenderTexture.active = active);
            Attempt(() => UnityEngine.GL.sRGBWrite = srgb);
            Attempt(() =>
            {
                var currentSelection = UnityEditor.Selection.objects;
                bool sameSelection = currentSelection.Length == selection.Length;
                for (int i = 0; sameSelection && i < selection.Length; i++)
                    sameSelection = currentSelection[i] == selection[i];
                if (!sameSelection) UnityEditor.Selection.objects = selection;
            });
            Attempt(() =>
            {
                if (UnityEditor.EditorGUIUtility.systemCopyBuffer != clipboard)
                    UnityEditor.EditorGUIUtility.systemCopyBuffer = clipboard;
            });
            for (int i = 0; i < preferenceKeys.Length; i++)
            {
                int index = i;
                Attempt(() =>
                {
                    if (preferenceExisted[index]) UnityEditor.EditorPrefs.SetString(preferenceKeys[index], preferenceValues[index]);
                    else UnityEditor.EditorPrefs.DeleteKey(preferenceKeys[index]);
                });
            }
            Attempt(() =>
            {
                if (focus != null && UnityEditor.EditorWindow.focusedWindow != focus) focus.Focus();
            });
            Attempt(() =>
            {
                for (int i = 0; i < preferenceKeys.Length; i++)
                    if (UnityEditor.EditorPrefs.HasKey(preferenceKeys[i]) != preferenceExisted[i] ||
                        (preferenceExisted[i] && UnityEditor.EditorPrefs.GetString(preferenceKeys[i]) != preferenceValues[i]))
                        throw new System.InvalidOperationException("Preference changed after restoration/focus: " + preferenceKeys[i] + "; cleanup uncertain.");
            });
            if (errors.Count > 0)
            {
                disposalFailure = new System.AggregateException("Owned fixture cleanup/restoration failed; unresolved owned-window/session cleanup remains uncertain", errors);
                throw disposalFailure;
            }
        }
    }
}
