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
        private static readonly UTF8Encoding utf8WithoutBom = new UTF8Encoding(false);
        private static readonly List<Session> sessions = new List<Session>();
        private static Process installProcess;
        private static double nextSessionPoll;
        private static string pendingCodePath;
        private static string pendingEditorPath;
        private static string pendingProfilePath;
        private static string pendingExtensionsPath;

        internal static event Action<ShaderFX> CodeChanged;

        internal static bool HasVsCode => !string.IsNullOrEmpty(FindVsCodePath());

        internal static void OpenInUnityEditor(ShaderFX effect)
        {
            try
            {
                string codePath = PrepareSession(effect);
                IExternalCodeEditor editor = CodeEditor.Editor.CurrentCodeEditor;
                if (editor == null)
                {
                    EditorUtility.DisplayDialog("Open FX Code", "No script editor is selected in Unity Preferences.", "OK");
                    return;
                }
                editor.OpenProject(codePath, 1, 1);
            }
            catch (Exception error) { EditorUtility.DisplayDialog("Open FX Code", error.Message, "OK"); }
        }

        internal static void OpenInVsCode(ShaderFX effect)
        {
            string editorPath = FindVsCodePath();
            if (string.IsNullOrEmpty(editorPath)) return;

            string codePath = PrepareSession(effect);
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
            if (File.Exists(installedMarker) && File.ReadAllText(installedMarker).Trim() == ExtensionVersion &&
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

        private static string PrepareSession(ShaderFX effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            Session session = FindSession(effect);
            if (session == null)
            {
                string documentPath = WhimTexDocumentService.PathOf(TextureCompositorWindow.FindFXTransformDocument(effect));
                string identity = (documentPath ?? effect.SourcePath ?? string.Empty) + "|" + effect.ShaderKey;
                string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "WhimTex", "ExternalCode");
                string stem = Hash(identity);
                string codePath = Path.Combine(folder, stem + ".hlsl");
                string baselinePath = Path.Combine(folder, stem + ".baseline");
                Directory.CreateDirectory(folder);
                string modelCode = effect.Code ?? string.Empty;
                if (!File.Exists(codePath) || !File.Exists(baselinePath))
                {
                    WritePair(codePath, baselinePath, modelCode);
                }
                else
                {
                    ResolveInitialChanges(effect, codePath, baselinePath, modelCode);
                }
                session = new Session(effect, codePath, baselinePath);
                sessions.Add(session);
                EditorApplication.update -= PollSessions;
                EditorApplication.update += PollSessions;
            }
            SynchronizeBeforeOpen(session);
            return session.CodePath;
        }

        private static void ResolveInitialChanges(ShaderFX effect, string codePath, string baselinePath, string modelCode)
        {
            var state = new CodeState(modelCode, File.ReadAllText(baselinePath), File.ReadAllText(codePath));
            if (state.ExternalChanged && !state.ModelChanged)
            {
                if (ApplyExternalCode(effect, state.External))
                    File.WriteAllText(baselinePath, state.External, utf8WithoutBom);
            }
            else if (state.ModelChanged && !state.ExternalChanged)
                WritePair(codePath, baselinePath, state.Model);
            else if (state.HasConflict)
            {
                int choice = EditorUtility.DisplayDialogComplex("External FX Code Changed",
                    "Both the document and its external code file changed since the last synchronization.",
                    "Use Document", "Cancel", "Use External File");
                if (choice == 0) WritePair(codePath, baselinePath, state.Model);
                else if (choice == 2 && ApplyExternalCode(effect, state.External))
                    File.WriteAllText(baselinePath, state.External, utf8WithoutBom);
            }
            else if (state.ExternalChanged)
                File.WriteAllText(baselinePath, state.External, utf8WithoutBom);
        }

        private static void SynchronizeBeforeOpen(Session session)
        {
            if (!session.TryGetEffect(out ShaderFX effect) || effect == null) return;
            CodeState state = session.ReadCodeState(effect);
            if (state.HasConflict)
            {
                ResolveInitialChanges(effect, session.CodePath, session.BaselinePath, state.Model);
                session.ResetObservation();
            }
            else if (state.ModelChanged)
                WritePair(session.CodePath, session.BaselinePath, state.Model);
            else if (state.ExternalChanged)
                ImportExternal(session, effect, state.External);
        }

        private static void PollSessions()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextSessionPoll) return;
            nextSessionPoll = now + SessionPollInterval;
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                Session session = sessions[i];
                if (!session.TryGetEffect(out ShaderFX effect) || effect == null)
                {
                    sessions.RemoveAt(i);
                    continue;
                }
                try { PollSession(session, effect); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            if (sessions.Count == 0) EditorApplication.update -= PollSessions;
        }

        private static void PollSession(Session session, ShaderFX effect)
        {
            CodeState state = session.ReadCodeState(effect);
            if (File.Exists(session.ApplyRequestPath))
            {
                ProcessApplyRequest(session, effect, state);
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
                else if (choice == 2) ImportExternal(session, effect, state.External);
            }
            else ImportExternal(session, effect, state.External);
        }

        private static int ShowSessionConflict(Session session, long writeTicks)
        {
            session.LastConflictTicks = writeTicks;
            return EditorUtility.DisplayDialogComplex("External FX Code Changed",
                "The document and external code file changed at the same time.",
                "Use Document", "Later", "Use External File");
        }

        private static void ProcessApplyRequest(Session session, ShaderFX effect, CodeState state)
        {
            if (WhimTexApi.IsShaderFXContentLocked(effect)) return;

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
                if (!ImportExternal(session, effect, state.External)) return;
            }
            else if (state.ModelChanged && !state.ExternalChanged)
            {
                if (!TryDeleteApplyRequest(session)) return;
                WritePair(session.CodePath, session.BaselinePath, state.Model);
                return;
            }
            else if (state.ExternalChanged && !ImportExternal(session, effect, state.External))
                return;

            if (!TryDeleteApplyRequest(session)) return;
            effect.Apply();
            CodeChanged?.Invoke(effect);
        }

        private static bool ImportExternal(Session session, ShaderFX effect, string external)
        {
            if (!string.Equals(effect.Code, external, StringComparison.Ordinal) && !ApplyExternalCode(effect, external))
                return false;
            File.WriteAllText(session.BaselinePath, external, utf8WithoutBom);
            session.ResetObservation();
            CodeChanged?.Invoke(effect);
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

        private static bool ApplyExternalCode(ShaderFX effect, string code)
        {
            if (effect == null || WhimTexApi.IsShaderFXContentLocked(effect)) return false;
            Undo.RecordObject(effect, "Edit Shader FX Code Externally");
            effect.SetDraftCode(code);
            Undo.FlushUndoRecordObjects();
            EditorUtility.SetDirty(effect);
            return true;
        }

        private static void WritePair(string codePath, string baselinePath, string content)
        {
            content ??= string.Empty;
            File.WriteAllText(codePath, content, utf8WithoutBom);
            File.WriteAllText(baselinePath, content, utf8WithoutBom);
        }

        private static Session FindSession(ShaderFX effect)
        {
            for (int i = 0; i < sessions.Count; i++)
                if (sessions[i].TryGetEffect(out ShaderFX current) && current == effect)
                    return sessions[i];
            return null;
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
                File.WriteAllText(Path.Combine(pendingExtensionsPath, ExtensionId + ".installed"), ExtensionVersion, utf8WithoutBom);
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

        private sealed class Session
        {
            private readonly WeakReference<ShaderFX> effect;
            internal readonly string CodePath;
            internal readonly string BaselinePath;
            internal readonly string ApplyRequestPath;
            internal long PendingWriteTicks;
            internal long LastConflictTicks;
            internal int StableWriteCount;

            internal Session(ShaderFX effect, string codePath, string baselinePath)
            {
                this.effect = new WeakReference<ShaderFX>(effect);
                CodePath = codePath;
                BaselinePath = baselinePath;
                ApplyRequestPath = codePath + ".apply";
                ResetObservation();
            }

            internal bool TryGetEffect(out ShaderFX value) => effect.TryGetTarget(out value);
            internal CodeState ReadCodeState(ShaderFX value) => new CodeState(value.Code ?? string.Empty,
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
