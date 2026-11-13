using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.CodeEditor;
using UnityEditor;
using UnityPackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXExternalCode
    {
        private const string ExtensionId = "dcfapixels.whimtex-fx-tools";
        private const string ExtensionVersion = "0.1.3";
        private const double SessionPollInterval = 0.35d;
        private const double CacheMaintenanceInterval = 3600d;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(1);
        private static readonly string[] CacheSuffixes = { ".hlsl", ".baseline", ".hlsl.apply" };
        private static double nextCacheMaintenance;
        private static readonly UTF8Encoding utf8WithoutBom = new UTF8Encoding(false);
        private static readonly List<Session> sessions = new List<Session>();
        private static Process installProcess;
        private static double nextSessionPoll;
        private static string pendingCodePath;
        private static string pendingEditorPath;
        private static string pendingProfilePath;
        private static string pendingExtensionsPath;
        private static string pendingExtensionStamp;

        internal static event Action<ShaderFX> CodeChanged;

        private static string CacheFolder => Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            "Library", "WhimTex", "ExternalCode");

        [InitializeOnLoadMethod]
        private static void InitializeCacheMaintenance()
        {
            EditorApplication.update -= MaintainCache;
            EditorApplication.update += MaintainCache;
            AssemblyReloadEvents.beforeAssemblyReload -= TouchActiveSessions;
            AssemblyReloadEvents.beforeAssemblyReload += TouchActiveSessions;
            EditorApplication.quitting -= TouchActiveSessions;
            EditorApplication.quitting += TouchActiveSessions;
        }

        private static void TouchActiveSessions()
        {
            foreach (Session session in sessions)
                if (session.TryGetTarget(out _))
                    TouchSession(session, DateTime.UtcNow);
        }

        private static void TouchSession(Session session, DateTime now)
        {
            // Baseline content stays unchanged. Do not touch HLSL's observed write time.
            try
            {
                if (File.Exists(session.BaselinePath)) File.SetLastWriteTimeUtc(session.BaselinePath, now);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void MaintainCache()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextCacheMaintenance || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextCacheMaintenance = now + CacheMaintenanceInterval;
            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach (Session session in sessions)
            {
                if (!session.TryGetTarget(out _)) continue;
                active.Add(Path.GetFileNameWithoutExtension(session.CodePath));
                TouchSession(session, DateTime.UtcNow);
            }
            CleanupCacheDirectory(CacheFolder, DateTime.UtcNow, active);
        }

        internal static int CleanupCacheDirectory(string folder, DateTime utcNow, ISet<string> activeStems)
        {
            int deleted = 0;
            try
            {
                if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return 0;
                folder = Path.GetFullPath(folder);
                var stems = new HashSet<string>(StringComparer.Ordinal);
                foreach (string path in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(path);
                    if (name.Length <= 32 || !CacheSuffixes.Contains(name.Substring(32))) continue;
                    string stem = name.Substring(0, 32);
                    if (stem.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) stems.Add(stem);
                }
                foreach (string stem in stems)
                {
                    if (activeStems != null && activeStems.Contains(stem)) continue;
                    try
                    {
                        string[] paths = CacheSuffixes.Select(suffix => Path.Combine(folder, stem + suffix)).ToArray();
                        bool expired = true;
                        foreach (string path in paths)
                        {
                            if (Directory.Exists(path)) { expired = false; break; }
                            if (!File.Exists(path)) continue;
                            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                                File.GetLastWriteTimeUtc(path) >= utcNow - CacheLifetime)
                            { expired = false; break; }
                        }
                        if (!expired) continue;
                        // Only owned, top-level files; no recursion and no profile cleanup.
                        foreach (string path in paths)
                            if (File.Exists(path)) { File.Delete(path); deleted++; }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return deleted;
        }

        internal static void OpenInUnityEditor(ShaderFX effect) => OpenInUnityEditor(new Target(effect, null));
        internal static void OpenInUnityEditor(WhimTexDocument document, string layerId) => OpenInUnityEditor(new Target(document, layerId));
        internal static void OpenInVsCode(ShaderFX effect) => OpenInVsCode(new Target(effect, null));
        internal static void OpenInVsCode(WhimTexDocument document, string layerId) => OpenInVsCode(new Target(document, layerId));

        private static void OpenInUnityEditor(Target target)
        {
            try
            {
                IExternalCodeEditor editor = CodeEditor.Editor.CurrentCodeEditor;
                if (editor == null || !editor.OpenProject(PrepareSession(target), 1, 1))
                    EditorUtility.DisplayDialog("Open Shader Code", "Could not open the file with the script editor selected in Unity Preferences.", "OK");
            }
            catch (Exception error) { EditorUtility.DisplayDialog("Open Shader Code", error.Message, "OK"); }
        }

        private static void OpenInVsCode(Target target)
        {
            try
            {
                string editorPath = FindVsCodePath();
                if (string.IsNullOrEmpty(editorPath))
                    EditorUtility.DisplayDialog("Open Shader Code", "VS Code could not be found.", "OK");
                else OpenPathInVsCode(editorPath, PrepareSession(target));
            }
            catch (Exception error) { EditorUtility.DisplayDialog("Open Shader Code", error.Message, "OK"); }
        }

        internal static void OpenPresetSource(string sourcePath)
        {
            try
            {
                if (!ShaderPresetIO.IsPresetPath(sourcePath)) throw new InvalidOperationException("Choose a .hlsl or .whimtexshader preset source.");
                string path = PresetLibraryPaths.PhysicalPath(sourcePath);
                if (!File.Exists(path)) throw new FileNotFoundException("The preset source file could not be found.", path);
                if (ShaderPresetIO.IsProgramPath(path))
                {
                    // A Graph preset is structured JSON, never a generated HLSL editing session.
                    var jsonEditor = CodeEditor.Editor.CurrentCodeEditor;
                    if (jsonEditor == null || !jsonEditor.OpenProject(path, 1, 1))
                        EditorUtility.DisplayDialog("Open Shader Preset", "Could not open the preset with the script editor selected in Unity Preferences.", "OK");
                    return;
                }
                string editorPath = FindVsCodePath();
                if (!string.IsNullOrEmpty(editorPath)) OpenPathInVsCode(editorPath, path);
                else
                {
                    IExternalCodeEditor editor = CodeEditor.Editor.CurrentCodeEditor;
                    if (editor == null || !editor.OpenProject(path, 1, 1))
                        EditorUtility.DisplayDialog("Open FX Preset", "Could not open the preset with the script editor selected in Unity Preferences.", "OK");
                }
            }
            catch (Exception error) { EditorUtility.DisplayDialog("Open FX Preset", error.Message, "OK"); }
        }

        private static void OpenPathInVsCode(string editorPath, string codePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string profilePath = Path.Combine(projectRoot, "Library", "WhimTex", "VSCodeProfile");
            string extensionsPath = Path.Combine(profilePath, "extensions");
            string packagePath = UnityPackageInfo.FindForAssembly(typeof(ShaderFXExternalCode).Assembly)?.resolvedPath;
            string vsixPath = string.IsNullOrEmpty(packagePath) ? null : Path.Combine(packagePath, "ExternalTools~", "WhimTexVSCode", "whimtex-fx-tools.vsix");
            if (string.IsNullOrEmpty(vsixPath) || !File.Exists(vsixPath))
            {
                EditorUtility.DisplayDialog("Open FX Code", "The bundled WhimTex language support package is missing.", "OK");
                return;
            }

            string installedMarker = Path.Combine(extensionsPath, ExtensionId + ".installed");
            string extensionPackage = Path.Combine(extensionsPath, ExtensionId + "-" + ExtensionVersion);
            string extensionStamp;
            using (var archive = File.OpenRead(vsixPath))
            using (var sha = SHA256.Create())
                extensionStamp = ExtensionVersion + ":" + BitConverter.ToString(sha.ComputeHash(archive)).Replace("-", string.Empty);
            if (File.Exists(installedMarker) && File.ReadAllText(installedMarker).Trim() == extensionStamp &&
                File.Exists(Path.Combine(extensionPackage, "package.json")))
            {
                LaunchVsCode(editorPath, profilePath, extensionsPath, codePath);
                return;
            }

            if (installProcess != null && !installProcess.HasExited)
            {
                pendingCodePath = codePath;
                return;
            }

            try
            {
                Directory.CreateDirectory(profilePath);
                Directory.CreateDirectory(extensionsPath);
                installProcess = StartVsCodeProcess(editorPath, "--user-data-dir", profilePath, "--extensions-dir", extensionsPath,
                    "--install-extension", vsixPath, "--force");
                if (installProcess == null) throw new InvalidOperationException("Could not start the VS Code extension installer.");
                pendingEditorPath = editorPath;
                pendingCodePath = codePath;
                pendingProfilePath = profilePath;
                pendingExtensionsPath = extensionsPath;
                pendingExtensionStamp = extensionStamp;
                EditorApplication.update -= PollInstall;
                EditorApplication.update += PollInstall;
            }
            catch (Exception error)
            {
                installProcess?.Dispose();
                installProcess = null;
                EditorUtility.DisplayDialog("Open FX Code", "Could not install bundled language support: " + error.Message, "OK");
            }
        }

        private static string PrepareSession(Target target)
        {
            if (!target.Exists) throw new InvalidOperationException("The shader is no longer in the document.");
            if (target.Mode == ShaderProgramMode.Graph)
                throw new InvalidOperationException("Open the Graph editor to edit a Graph program.");
            if (target.IsLinked)
                throw new InvalidOperationException("Open the preset source or embed a copy before editing its Code.");
            var ownerId = UnityObjectID.FromObject(target.Owner);
            Session session = sessions.Find(session => session.Matches(ownerId, target.LayerId));
            if (session == null)
            {
                string folder = CacheFolder;
                string stem = Hash(target.CacheIdentity());
                string codePath = Path.Combine(folder, stem + ".hlsl");
                string baselinePath = Path.Combine(folder, stem + ".baseline");
                Directory.CreateDirectory(folder);
                if (!File.Exists(codePath) || !File.Exists(baselinePath))
                    WritePair(codePath, baselinePath, target.Code);
                session = new Session(ownerId, target.LayerId, codePath, baselinePath);
                sessions.Add(session);
                EditorApplication.update -= PollSessions;
                EditorApplication.update += PollSessions;
            }
            SynchronizeBeforeOpen(session, target);
            TouchSession(session, DateTime.UtcNow);
            return session.CodePath;
        }

        private static void SynchronizeBeforeOpen(Session session, Target target)
        {
            CodeState state = session.ReadCodeState(target);
            if (state.HasConflict)
            {
                int choice = EditorUtility.DisplayDialogComplex("External Shader Code Changed",
                    "Both the document and its external code file changed since the last synchronization.",
                    "Use Document", "Cancel", "Use External File");
                if (choice == 0) WritePair(session.CodePath, session.BaselinePath, state.Model);
                else if (choice == 2) ImportExternal(session, target, state.External);
                session.ResetObservation();
            }
            else if (state.ModelChanged)
                WritePair(session.CodePath, session.BaselinePath, state.Model);
            else if (state.ExternalChanged)
                ImportExternal(session, target, state.External);
        }

        private static void PollSessions()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextSessionPoll) return;
            nextSessionPoll = now + SessionPollInterval;
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                Session session = sessions[i];
                if (!session.TryGetTarget(out Target target))
                {
                    sessions.RemoveAt(i);
                    continue;
                }
                try { PollSession(session, target); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (sessions.Count == 0) EditorApplication.update -= PollSessions;
        }

        private static void PollSession(Session session, Target target)
        {
            if (target.Mode != ShaderProgramMode.Code || target.IsLinked) return;
            CodeState state = session.ReadCodeState(target);
            if (File.Exists(session.ApplyRequestPath))
            {
                ProcessApplyRequest(session, target, state);
                return;
            }

            if (!state.ExternalChanged)
            {
                if (state.ModelChanged) WritePair(session.CodePath, session.BaselinePath, state.Model);
                return;
            }

            long ticks = File.GetLastWriteTimeUtc(session.CodePath).Ticks;
            if (ticks != session.PendingWriteTicks)
            {
                session.PendingWriteTicks = ticks;
                session.StableWriteCount = 0;
                return;
            }
            if (++session.StableWriteCount < 2 || ticks == session.LastConflictTicks) return;

            if (state.HasConflict)
            {
                int choice = ShowSessionConflict(session, ticks);
                if (choice == 0) WritePair(session.CodePath, session.BaselinePath, state.Model);
                else if (choice == 2) ImportExternal(session, target, state.External);
            }
            else ImportExternal(session, target, state.External);
        }

        private static int ShowSessionConflict(Session session, long writeTicks)
        {
            session.LastConflictTicks = writeTicks;
            return EditorUtility.DisplayDialogComplex("External Shader Code Changed",
                "The document and external code file changed at the same time.",
                "Use Document", "Later", "Use External File");
        }

        private static void ProcessApplyRequest(Session session, Target target, CodeState state)
        {
            if (target.IsLocked) return;

            if (state.HasConflict)
            {
                long ticks = File.GetLastWriteTimeUtc(session.CodePath).Ticks;
                if (ticks == session.LastConflictTicks) return;
                int choice = ShowSessionConflict(session, ticks);
                if (choice == 0)
                {
                    if (!TryDeleteApplyRequest(session)) return;
                    WritePair(session.CodePath, session.BaselinePath, state.Model);
                    return;
                }
                if (choice != 2) return;
                if (!ImportExternal(session, target, state.External)) return;
            }
            else if (state.ModelChanged && !state.ExternalChanged)
            {
                if (!TryDeleteApplyRequest(session)) return;
                WritePair(session.CodePath, session.BaselinePath, state.Model);
                return;
            }
            else if (state.ExternalChanged && !ImportExternal(session, target, state.External))
                return;

            if (!TryDeleteApplyRequest(session)) return;
            target.Compile();
        }

        private static bool ImportExternal(Session session, Target target, string external)
        {
            bool changed = !string.Equals(target.Code, external, StringComparison.Ordinal);
            if (changed && target.IsLocked)
                return false;
            if (changed) target.SetCode(external);
            File.WriteAllText(session.BaselinePath, external, utf8WithoutBom);
            session.ResetObservation();
            target.NotifyFX();
            return true;
        }

        private static bool TryDeleteApplyRequest(Session session)
        {
            try
            {
                if (File.Exists(session.ApplyRequestPath)) File.Delete(session.ApplyRequestPath);
                return !File.Exists(session.ApplyRequestPath);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void WritePair(string codePath, string baselinePath, string content)
        {
            content ??= string.Empty;
            File.WriteAllText(codePath, content, utf8WithoutBom);
            File.WriteAllText(baselinePath, content, utf8WithoutBom);
        }

        private static void PollInstall()
        {
            if (installProcess == null || !installProcess.HasExited) return;
            EditorApplication.update -= PollInstall;
            int exitCode = installProcess.ExitCode;
            installProcess.Dispose();
            installProcess = null;
            if (exitCode == 0)
            {
                Directory.CreateDirectory(pendingExtensionsPath);
                File.WriteAllText(Path.Combine(pendingExtensionsPath, ExtensionId + ".installed"), pendingExtensionStamp, utf8WithoutBom);
                LaunchVsCode(pendingEditorPath, pendingProfilePath, pendingExtensionsPath, pendingCodePath);
            }
            else
                EditorUtility.DisplayDialog("Open FX Code", "VS Code did not install the bundled WhimTex language support package.", "OK");
        }

        private static void LaunchVsCode(string editorPath, string profilePath, string extensionsPath, string codePath)
        {
            try
            {
                StartVsCodeProcess(editorPath, "--new-window", "--user-data-dir", profilePath, "--extensions-dir", extensionsPath,
                    "--goto", codePath + ":1:1");
            }
            catch (Exception error) { EditorUtility.DisplayDialog("Open FX Code", error.Message, "OK"); }
        }

        private static Process StartVsCodeProcess(string editorPath, params string[] arguments)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor &&
                (Path.GetExtension(editorPath).Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                 Path.GetExtension(editorPath).Equals(".bat", StringComparison.OrdinalIgnoreCase)))
            {
                string commandLine = "\"" + editorPath + "\" " + Arguments(arguments);
                return Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    Arguments = "/d /s /c \"" + commandLine + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }

            return Process.Start(new ProcessStartInfo
            {
                FileName = editorPath,
                Arguments = Arguments(arguments),
                UseShellExecute = false
            });
        }

        private static string FindVsCodePath()
        {
            Dictionary<string, string> unityEditors = CodeEditor.Editor?.GetFoundScriptEditorPaths();
            if (unityEditors != null)
            foreach (KeyValuePair<string, string> entry in unityEditors)
            {
                string name = entry.Value ?? string.Empty;
                string path = entry.Key ?? string.Empty;
                if (name.IndexOf("Visual Studio Code", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.Equals("VS Code", StringComparison.OrdinalIgnoreCase) ||
                    IsCodeExecutable(path))
                    return path;
            }

            string pathCommand = FindCommandOnPath("code") ?? FindCommandOnPath("code-insiders");
            if (!string.IsNullOrEmpty(pathCommand)) return pathCommand;

            string configured = WhimTexUserSettings.VsCodeExecutable;
            if (string.IsNullOrWhiteSpace(configured)) return null;
            if (Path.IsPathFullyQualified(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
            return FindCommandOnPath(configured);
        }

        private static string FindCommandOnPath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            command = command.Trim();
            string[] names = CommandFileNames(command);
            string pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathValue)) return null;
            string[] directories = pathValue.Split(Path.PathSeparator);
            for (int directoryIndex = 0; directoryIndex < directories.Length; directoryIndex++)
            {
                string directory = directories[directoryIndex].Trim().Trim('"');
                if (directory.Length == 0 || !Path.IsPathRooted(directory)) continue;
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    string candidate;
                    try { candidate = Path.Combine(directory, names[nameIndex]); }
                    catch (ArgumentException) { continue; }
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }
            return null;
        }

        private static string[] CommandFileNames(string command)
        {
            if (!string.IsNullOrEmpty(Path.GetExtension(command))) return new[] { command };
            if (Application.platform != RuntimePlatform.WindowsEditor) return new[] { command };

            string extensions = Environment.GetEnvironmentVariable("PATHEXT");
            if (string.IsNullOrWhiteSpace(extensions)) extensions = ".COM;.EXE;.BAT;.CMD";
            var names = new List<string>();
            foreach (string extension in extensions.Split(';'))
            {
                string normalized = extension.Trim();
                if (normalized.Length == 0) continue;
                if (normalized[0] != '.') normalized = "." + normalized;
                AddCommandCandidate(names, command + normalized);
            }
            AddCommandCandidate(names, command + ".cmd");
            AddCommandCandidate(names, command + ".exe");
            AddCommandCandidate(names, command + ".bat");
            return names.ToArray();
        }

        private static void AddCommandCandidate(List<string> names, string candidate)
        {
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], candidate, StringComparison.OrdinalIgnoreCase)) return;
            names.Add(candidate);
        }

        private static bool IsCodeExecutable(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string file = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string extension = Path.GetExtension(file);
            string name = Path.GetFileNameWithoutExtension(file);
            return (name.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("code-insiders", StringComparison.OrdinalIgnoreCase)) &&
                ((string.IsNullOrEmpty(extension) && Application.platform != RuntimePlatform.WindowsEditor) ||
                    extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".com", StringComparison.OrdinalIgnoreCase));
        }

        private static string Arguments(params string[] values) => string.Join(" ", values.Select(CodeEditor.QuoteForProcessStart));

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(32);
                for (int i = 0; i < 16; i++) result.Append(bytes[i].ToString("x2"));
                return result.ToString();
            }
        }

        private readonly struct CodeState
        {
            internal readonly string Model;
            internal readonly string External;
            internal readonly bool ModelChanged;
            internal readonly bool ExternalChanged;
            internal bool HasConflict => ModelChanged && ExternalChanged &&
                !string.Equals(Model, External, StringComparison.Ordinal);

            internal CodeState(string model, string baseline, string external)
            {
                Model = model;
                External = external;
                ModelChanged = !string.Equals(model, baseline, StringComparison.Ordinal);
                ExternalChanged = !string.Equals(external, baseline, StringComparison.Ordinal);
            }
        }

        private readonly struct Target
        {
            internal readonly UnityEngine.Object Owner;
            internal readonly string LayerId;
            private readonly ShaderLayerBehaviour layer;

            internal Target(UnityEngine.Object owner, string layerId)
            {
                Owner = owner;
                LayerId = layerId;
                layer = owner is WhimTexDocument document && document != null && layerId != null
                    ? document.FindLayer(layerId)?.Behaviour as ShaderLayerBehaviour : null;
            }

            private ShaderFX Effect => Owner as ShaderFX;
            private WhimTexDocument Document => Owner as WhimTexDocument;
            internal bool Exists => Effect != null || layer != null;
            internal ShaderProgramMode Mode => layer != null ? layer.mode : Effect.Mode;
            internal bool IsLinked => layer != null ? ShaderPresetLink.IsLinked(layer.program) : Effect.IsCatalogLinked;
            internal bool IsLocked => layer != null ? WhimTexApi.IsLayerContentLocked(Document, layer.Owner) : WhimTexApi.IsShaderFXContentLocked(Effect);
            internal string Code => (layer != null ? layer.draftCode : Effect.Code) ?? string.Empty;

            internal string CacheIdentity()
            {
                if (layer != null)
                {
                    string path = WhimTexDocumentService.PathOf(Document);
                    return "shader-layer|" + (path ?? UnityObjectID.FromObject(Document).ToString()) + "|" + LayerId;
                }
                string documentPath = WhimTexDocumentService.PathOf(WhimTexWindow.FindFXTransformDocument(Effect));
                return (documentPath ?? Effect.SourcePath ?? string.Empty) + "|" + Effect.ShaderKey;
            }

            internal void SetCode(string code)
            {
                Undo.RecordObject(Owner, "Edit Shader Code Externally");
                if (layer != null)
                {
                    layer.draftCode = code;
                    Document.MarkChanged();
                }
                else
                {
                    Effect.SetDraftCode(code);
                    EditorUtility.SetDirty(Effect);
                }
                Undo.FlushUndoRecordObjects();
            }

            internal void Compile()
            {
                if (layer != null)
                {
                    Undo.RecordObject(Document, "Compile Shader Code");
                    layer.CompileCode(Document);
                    Document.MarkChanged();
                    Undo.FlushUndoRecordObjects();
                }
                else
                {
                    Effect.Compile();
                    NotifyFX();
                }
            }

            internal void NotifyFX()
            {
                if (Effect != null) CodeChanged?.Invoke(Effect);
            }
        }

        private sealed class Session
        {
            private readonly UnityObjectID owner;
            private readonly string layerId;
            internal readonly string CodePath;
            internal readonly string BaselinePath;
            internal readonly string ApplyRequestPath;
            internal long PendingWriteTicks;
            internal long LastConflictTicks;
            internal int StableWriteCount;

            internal Session(UnityObjectID owner, string layerId, string codePath, string baselinePath)
            {
                this.owner = owner;
                this.layerId = layerId;
                CodePath = codePath;
                BaselinePath = baselinePath;
                ApplyRequestPath = codePath + ".apply";
                ResetObservation();
            }

            internal bool Matches(UnityObjectID value, string id) => owner == value && layerId == id;

            internal bool TryGetTarget(out Target target)
            {
                target = new Target(owner.Resolve(), layerId);
                return target.Exists;
            }

            internal CodeState ReadCodeState(Target target) => new CodeState(target.Code,
                File.Exists(BaselinePath) ? File.ReadAllText(BaselinePath) : string.Empty,
                File.Exists(CodePath) ? File.ReadAllText(CodePath) : string.Empty);

            internal void ResetObservation()
            {
                PendingWriteTicks = File.Exists(CodePath) ? File.GetLastWriteTimeUtc(CodePath).Ticks : 0;
                StableWriteCount = 0;
                LastConflictTicks = 0;
            }
        }
    }
}
