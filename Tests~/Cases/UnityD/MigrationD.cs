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
        bool disposed;
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
            if (disposed) return;
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
            Attempt(() =>
            {
                if (focus != null && UnityEditor.EditorWindow.focusedWindow != focus) focus.Focus();
            });
            if (errors.Count > 0) throw new System.AggregateException("Owned fixture cleanup/restoration failed", errors);
        }
    }
}
