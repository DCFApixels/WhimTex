using System;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private bool SaveJsonToPath(string path, WhimTexJsonWriteMode mode)
        {
            PrepareDocumentSave();
            WhimTexDocument copy = null;
            try
            {
                if (!ConfirmIncompleteDocumentSave(activeDocument, ref path, out bool allowDataLoss)) return false;
                var options = new WhimTexJsonWriteOptions { Mode = mode, AllowDrawingOmission = true, AllowDataLoss = allowDataLoss };
                var written = WhimTexDocumentJson.Write(activeDocument, options);
                if (!ConfirmJsonDrawingOmission(written, false)) return false;
                using var operation = new WhimTexDocumentOperation("Save WhimTex JSON");
                if (!written.DrawingPixelsOmitted)
                {
                    WhimTexDocumentFile.SaveJson(activeDocument, path, options);
                    BindDocumentFile(path);
                    temporaryDocumentDirty = false;
                    UpdateUnsavedChangesState();
                }
                else
                {
                    using var read = WhimTexDocumentJson.Read(written.Json);
                    copy = read.TakeDocument();
                    copy.name = activeDocument.name;
                    // Empty Drawing placeholders are expected here; no live pixel data is discarded in place.
                    WhimTexDocumentFile.SaveJsonSnapshot(activeDocument, copy, path, options);
                    WhimTexApi.TransferLiveDocument(agentSessionId, activeDocument, copy);
                    agentSessionDocument = copy;
                    SetDocument(copy);
                    copy = null;
                    BindDocumentFile(path);
                    temporaryDocumentDirty = false;
                    UpdateUnsavedChangesState();
                }
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                RefreshDocumentLoadWarning();
                if (asset != null) EditorGUIUtility.PingObject(asset);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error) { Debug.LogException(error); EditorUtility.DisplayDialog("WhimTex", error.Message, "OK"); return false; }
            finally { if (copy != null) DestroyImmediate(copy); }
        }

        private static bool ConfirmJsonDrawingOmission(WhimTexJsonWriteResult written, bool export)
            => !written.DrawingPixelsOmitted || EditorUtility.DisplayDialog("Save without Drawing pixels?",
                string.Join("\n", written.Warnings) + (export
                    ? "\n\nThe open document is not changed."
                    : "\n\nEmpty Drawing nodes and their settings will be retained. The open document will switch to the saved version without Drawing pixels. Save as TIFF instead to keep them."),
                "Save Without Pixels", "Cancel");
    }
}
