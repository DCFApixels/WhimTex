using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        public static string DocumentJsonFile(string requestPath)
            => Respond(() => DocumentJsonRequest(Parse(ReadRequestFile(requestPath))));

        public static string DocumentJson(string requestJson)
            => Respond(() => DocumentJsonRequest(Parse(requestJson)));

        private static JObject DocumentJsonRequest(JObject request)
        {
            Keys(request, "apiVersion", "action", "assetPath", "layerIds", "layerId", "json", "sourcePath", "destinationPath",
                "mode", "allowDrawingOmission", "expectedRevision", "save", "compile");
            Require(Int(request, "apiVersion", 0, 1, 1) == 1, "apiVersion must be 1.");
            string action = Text(request, "action");
            var options = new WhimTexJsonWriteOptions { Mode = Enum(request, "mode", WhimTexJsonWriteMode.FullOptimized),
                AllowDrawingOmission = Bool(request, "allowDrawingOmission") };
            JObject result = Success();
            if (action == "serialize" || action == "export")
            {
                var document = Load(DocumentPath(Text(request, "assetPath")));
                try
                {
                    WhimTexJsonWriteResult write;
                    if (request["layerIds"] is JArray selected)
                    {
                        var layers = Enumerate(document.layers).ToDictionary(layer => layer.Id);
                        var roots = selected.Select(id => layers.TryGetValue((string)id, out var layer) ? layer :
                            throw new WhimTexDocumentException("Layer not found: " + id)).ToList();
                        Require(action != "export", "Export writes a whole document; use serialize with layerIds for a fragment.");
                        write = WhimTexDocumentJson.WriteLayers(document, roots, options);
                    }
                    else write = WhimTexDocumentJson.Write(document, options);
                    if (action == "export")
                    {
                        string destination = DocumentPath(Text(request, "destinationPath"));
                        Require(!File.Exists(destination), "Export destination exists. Choose a new path.", "already_exists");
                        write = WhimTexDocumentFile.ExportJson(document, destination, options);
                        result["assetPath"] = destination;
                    }
                    else result["json"] = JObject.Parse(write.Json);
                    result["warnings"] = new JArray(write.Warnings);
                    return result;
                }
                finally { ReleaseTransientDocument(document); }
            }

            Require(action == "validate" || action == "write" || action == "insert" || action == "replace" || action == "open", "Unknown JSON action.");
            if (action == "open")
            {
                string path = DocumentPath(Text(request, "assetPath"));
                Require(WhimTexDocumentJson.IsDocumentFile(path), "Expected a WhimTex JSON document.");
                Require(WhimTexWindow.TryOpenWhimTexDocumentPath(path, out string error), error ?? "Could not open JSON document.", "open_failed");
                result["assetPath"] = path;
                return result;
            }
            Require((request["json"] != null) != (request["sourcePath"] != null), "Supply exactly one of json or sourcePath.");
            string json = request["json"] != null ? Obj(request["json"], "json").ToString() : File.ReadAllText(Text(request, "sourcePath"));
            string targetPath = action == "validate" ? null : DocumentPath(Text(request, "assetPath"));
            if (targetPath != null)
                Require(WhimTexDocumentService.FindDisplayed(targetPath) == null, "Use the assistant API for an open document.", "document_busy");
            using var build = action == "insert" || action == "replace" ? WhimTexDocumentBuild.Open(targetPath) : null;
            if (build != null)
                Require(Text(request, "expectedRevision") == Revision(build.Document), "Inspect and supply the current expectedRevision.", "revision_conflict");
            bool compile = Bool(request, "compile", action != "validate");
            using var parsed = build == null ? WhimTexDocumentJson.Read(json, compile)
                : WhimTexDocumentJson.ReadForInsertion(json, build.Document.width, build.Document.height, compile);
            result["warnings"] = new JArray(parsed.Warnings);
            if (action == "validate") return result;
            if (action == "write")
            {
                if (request["mode"] == null) options.Mode = parsed.Document.JsonWriteMode;
                bool save = Bool(request, "save", true);
                WhimTexDocument existing = null;
                string saved = targetPath;
                try
                {
                    if (File.Exists(targetPath))
                    {
                        existing = Load(targetPath);
                        Require(Text(request, "expectedRevision") == Revision(existing), "Inspect and supply the current expectedRevision before replacing a document.", "revision_conflict");
                        if (save) parsed.Document.documentBinding = existing.documentBinding;
                    }
                    if (save)
                        saved = WhimTexDocumentJson.IsJsonPath(targetPath)
                            ? WhimTexDocumentFile.SaveJson(parsed.Document, targetPath, options)
                            : WhimTexDocumentFile.Save(parsed.Document, targetPath);
                    else
                        WhimTexDocumentService.Bind(parsed.Document, targetPath);
                }
                finally
                {
                    if (existing != null && parsed.Document.documentBinding == existing.documentBinding) parsed.Document.documentBinding = null;
                    ReleaseTransientDocument(existing);
                }
                result["saved"] = save;
                result["assetPath"] = saved;
                result["document"] = Snapshot(parsed.Document, saved);
                return result;
            }
            var destinationDocument = build.Document;
            Layer replace = null;
            if (action == "replace")
            {
                Require(parsed.Document.layers.Count == 1, "replace requires exactly one root layer.");
                replace = Enumerate(destinationDocument.layers).FirstOrDefault(layer => layer.Id == Text(request, "layerId"));
                Require(replace != null, "Replacement target was not found.");
            }
            var copies = destinationDocument.CopyLayersFrom(parsed.Document, parsed.Document.layers, false, null);
            if (replace != null)
            {
                var prepared = copies[parsed.Document.layers[0]];
                destinationDocument.layers.Remove(prepared);
                foreach (var old in Enumerate(new List<Layer> { replace })) old.ReleaseTransientResources();
                replace.AdoptContent(prepared);
            }
            ValidateTargets(destinationDocument, targetPath);
            WhimTexDocumentJson.ValidateModelInputs(destinationDocument);
            WhimTexDocumentJson.CaptureMissingAssets(destinationDocument);
            if (Bool(request, "save", true))
            {
                if (WhimTexDocumentJson.IsJsonPath(targetPath)) WhimTexDocumentFile.SaveJson(destinationDocument, targetPath, options);
                else build.Save(targetPath);
            }
            result["saved"] = Bool(request, "save", true);
            result["document"] = Snapshot(destinationDocument, targetPath);
            return result;
        }
    }
}
