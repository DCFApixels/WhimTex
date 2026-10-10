using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static DCFApixels.WhimTex.AgentJson;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexApi
    {
        public static string Inspect(string assetPath) => Respond(() =>
        {
            string path = DocumentPath(assetPath);
            WhimTexDocument document = Load(path);
            try
            {
                Require(!WhimTexWindow.IsDocumentBusyForApi(document), "Finish the current paint/transform gesture first.", "document_busy");
                JObject result = Success();
                result["document"] = Snapshot(document, path);
                return result;
            }
            finally { ReleaseTransientDocument(document); }
        });

        public static string Describe() => Respond(() =>
        {
            JObject result = Success();
            result["operations"] = new JArray("add", "set", "transform", "target", "move", "stroke", "compact",
                "fx", "delete", "duplicate", "merge", "convertToDrawing", "blurStroke", "smudgeStroke", "healStroke", "resize");
            result["resize"] = "Document operation: width/height (1..16384, at most 16,777,216 pixels), preserveLayout=true. Preserves normalized layer transforms, not pixel-valued effects or source raster dimensions. Result layerId/name are null.";
            result["fxOperations"] = new JArray("add", "replace", "set", "remove", "move", "copy", "apply", "applyAll");
            result["fxCatalog"] = "whimtex_fx_catalog: query installed presets; pass presetId for parameter details. Use returned id in FX add/replace.";
            result["assistantBatch"] = "whimtex_assistant_execute: sessionId + expectedRevision + operations, same operations as batch/headless; one Undo step, no save. Finish active jobs first.";
            result["renderProbe"] = "whimtex_render_probe: exactly one assetPath/assistantSessionId/headlessSessionId; stage composite/layer/beforeFx/afterFx; channel rgba/r/g/b/a.";
            result["storageFormats"] = new JArray("tiff", "whimtex.document");
            result["documentJson"] = "whimtex_document_json: shared document/fragment schema; Full, FullOptimized (default), Compact. Drawing pixels require explicit omission permission. Assets resolve GUID/localId, then Path if GUID is missing.";
            result["backends"] = new JObject {
                ["tiff"] = "window-independent WhimTexDocumentBuild; transient model with atomic TIFF save",
                ["whimtex.document"] = "editable .json document; shared reader/writer for storage, clipboard and agents; no Drawing pixels"
            };
            result["diagnostics"] = new JObject {
                ["storage"] = "whimtex_storage_inspect / WhimTexApi.InspectStorage(assetPath): metadata-only TIFF block inspection",
                ["validate"] = "whimtex_document_validate / WhimTexApi.Validate(assetPath, render): structure, limits, references and Shader FX",
                ["shaderFxCompile"] = "whimtex_fx_compile / WhimTexApi.CompileFX(presetPath, source, includeBasePath): compile exactly one marked preset or raw WhimTex ApplyFX HLSL in a transient Unity shader; return warnings/errors without changing a document",
                ["status"] = "whimtex_document_status / WhimTexApi.Status(assetPath): disk revision, importer, dirty, lock and staged recovery state",
                ["compare"] = "whimtex_document_compare / WhimTexApi.Compare(leftPath, rightPath, render, maxSize): model, TIFF block and optional rendered-pixel comparison",
                ["recover"] = "whimtex_document_recover / WhimTexApi.Recover(sourcePath, destinationPath): validate and copy a staged TIFF to a new asset",
                ["export"] = "whimtex_document_export / WhimTexApi.Export(assetPath, outputPath, maxSize, overwrite): flattened PNG/JPEG/TGA/EXR export to Temp/WhimTex"
            };
            result["colorRanges"] = new JArray(System.Enum.GetNames(typeof(LayerColorRange)));
            result["blendRanges"] = new JArray(System.Enum.GetNames(typeof(LayerBlendRange)));
            result["channelMappingSources"] = new JArray(LayerChannelMapping.Labels);
            result["clippingMask"] = "Boolean setting on every layer type. Clips to the first non-clipping sibling below; missing/hidden bases hide the chain. Participating groups are isolated; base alpha and opacity are preserved.";
            result["groupCompositing"] = new JArray(System.Enum.GetNames(typeof(GroupCompositing)));
            var layerTypes = new JArray();
            foreach (var descriptor in LayerTypeRegistry.Entries) layerTypes.Add(descriptor.ApiId);
            result["layerTypes"] = layerTypes;
            result["blurDefaults"] = BlurSnapshot(new BlurLayerBehaviour());
            result["blurModes"] = new JArray(System.Enum.GetNames(typeof(BlurType)));
            result["sharpenDefaults"] = SharpenSnapshot(new SharpenLayerBehaviour());
            result["sharpenAlgorithms"] = new JArray(System.Enum.GetNames(typeof(SharpenLayerBehaviour.Algorithm)));
            result["sharpenChannels"] = new JArray(System.Enum.GetNames(typeof(SharpenLayerBehaviour.ChannelMode)));
            result["noiseDimensions"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.NoiseDimensions)));
            result["noisePeriodicAxes"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.PeriodicAxes)));
            result["noiseDefaults"] = NoiseSnapshot(new NoiseLayerBehaviour());
            result["fillModes"] = new JArray(System.Enum.GetNames(typeof(ColorFillLayerBehaviour.FillMode)));
            result["fillPatternDefaults"] = FillPatternSnapshot(new FillPatternSettings());
            result["shapeDefaults"] = ShapeSnapshot(new ShapeLayerBehaviour());
            result["shapeKinds"] = new JArray(System.Enum.GetNames(typeof(ShapeLayerBehaviour.ShapeKind)));
            result["textDefaults"] = TextSnapshot(new TextLayerBehaviour());
            result["systemFonts"] = new JArray(SystemFontCatalog.Names);
            result["textFontStyles"] = new JArray(System.Enum.GetNames(typeof(FontStyle)));
            result["textCasingModes"] = new JArray(System.Enum.GetNames(typeof(TextCasing)));
            result["textAlignments"] = new JArray(System.Enum.GetNames(typeof(TextAnchor)));
            result["textLayoutModes"] = new JArray(System.Enum.GetNames(typeof(TextLayoutMode)));
            result["textWrappingRules"] = new JArray(System.Enum.GetNames(typeof(TextWrapping)));
            result["noiseTypes"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.NoiseType)));
            result["noiseWhiteColors"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.WhiteNoiseColor)));
            result["noiseFractals"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.FractalType)));
            result["noiseCellularDistances"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.CellularDistance)));
            result["noiseCellularReturns"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.CellularReturn)));
            result["noiseWarps"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.WarpType)));
            result["noiseEncodings"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.OutputEncoding)));
            result["sdfEncodings"] = new JArray(System.Enum.GetNames(typeof(SDFLayerBehaviour.OutputEncoding)));
            result["normalMapDefaults"] = NormalMapSnapshot(new NormalMapLayerBehaviour());
            result["makeSeamlessDefaults"] = MakeSeamlessSnapshot(MakeSeamlessLayerBehaviour.CreateDefault());
            result["makeSeamlessModes"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.SeamlessMode)));
            result["makeSeamlessPoissonEdges"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.PoissonEdges)));
            result["makeSeamlessHorizontal"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.HorizontalDirection)));
            result["makeSeamlessVertical"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.VerticalDirection)));
            result["makeSeamlessQuiltingQuality"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.QuiltingQuality)));
            result["makeSeamlessQuiltingChannels"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.QuiltingChannels)));
            result["blurDirections"] = new JArray(System.Enum.GetNames(typeof(BlurLayerBehaviour.MotionDirection)));
            result["blurEdges"] = new JArray(System.Enum.GetNames(typeof(BlurLayerBehaviour.EdgeMode)));
            result["sharpenEdges"] = new JArray(System.Enum.GetNames(typeof(SharpenLayerBehaviour.EdgeMode)));
            result["blendModes"] = new JArray(System.Enum.GetNames(typeof(BlendMode)));
            result["tilingModes"] = new JArray(System.Enum.GetNames(typeof(TransformTilingMode)));
            result["filterModes"] = new JArray(System.Enum.GetNames(typeof(LayerFilterMode)));
            result["distanceMetrics"] = new JArray(System.Enum.GetNames(typeof(DistanceMetric)));
            result["effectInputModes"] = new JArray(System.Enum.GetNames(typeof(EffectInputMode)));
            result["repeatModes"] = new JArray(System.Enum.GetNames(typeof(PaintRepeatMode)));
            result["repeatElements"] = new JArray(System.Enum.GetNames(typeof(PaintRepeatElementMode)));
            result["repeatBoundaries"] = new JArray(System.Enum.GetNames(typeof(PaintRepeatBoundaryMode)));
            result["pencilShapes"] = new JArray(System.Enum.GetNames(typeof(PencilShape)));
            result["coordinates"] = "Layer index 0 is topmost. Transform position uses canvas pixels, +X right, +Y up; rotation is counterclockwise degrees. Pivot is bottom-left UV. canvasPixels stroke points use top-left origin; layerUv uses bottom-left UV.";
            result["limits"] = new JObject { ["requestBytes"] = 4194304, ["operations"] = 256, ["canvasPixels"] = MaxCanvasPixels,
                ["layers"] = 1024, ["drawingPixels"] = 67108864, ["strokePoints"] = 4096, ["strokeStamps"] = 100000, ["strokeCoveragePixels"] = 250000000,
                ["fxParameters"] = MaxFxParameters, ["fxPerLayer"] = 32, ["headlessSessions"] = 8 };
            result["editing"] = "Inspect before editing; expectedRevision is mandatory on existing TIFF/JSON documents. Use @aliases within a batch. New documents require a TIFF/JSON assetPath and save=true. Save failure may leave partial asset I/O: inspect before retrying.";
            result["storagePolicy"] = new JObject {
                ["newDocuments"] = "TIFF (*.tiff) or unified JSON (*.json)",
                ["readOperations"] = new JArray("whimtex_document_inspect", "whimtex_document_render", "whimtex_document_validate", "whimtex_document_status", "whimtex_document_export")
            };
            result["agentModes"] = new JObject {
                ["batch"] = new JObject {
                    ["command"] = "whimtex_batch_execute",
                    ["windowRequired"] = false,
                    ["persistence"] = "save=true writes TIFF/JSON; save=false discards the temporary model after returning. No user Undo of the file. Does not save unsaved Assistant changes. JSON cannot save Drawing pixels.",
                    ["assetPath"] = "TIFF/JSON for create/edit/save"
                },
                ["headlessLive"] = new JObject {
                    ["command"] = "whimtex_headless_live",
                    ["windowRequired"] = false,
                    ["assetPath"] = "TIFF only",
                    ["operations"] = new JArray("begin", "list", "status", "preview", "render", "complete", "cancel")
                },
                ["assistant"] = new JObject {
                    ["commands"] = new JArray("whimtex_assistant_begin", "whimtex_assistant_lock", "whimtex_assistant_sessions", "whimtex_assistant_live", "whimtex_assistant_execute"),
                    ["windowRequired"] = true,
                    ["assetPath"] = "Uses the currently open document"
                }
            };
            result["reference"] = "Documentation~/AgentAPI.md";
            result["liveEditing"] = new JObject {
                ["fastBegin"] = "whimtex_assistant_begin / WhimTexApi.LiveBegin(requestId, name, source, area, sessionId, sourceLayerId, selectionMode, padding)",
                ["selectionModes"] = new JArray("strict", "guide"),
                ["sessions"] = "whimtex_assistant_sessions / WhimTexApi.LiveSessions()",
                ["execute"] = "whimtex_assistant_live / WhimTexApi.LiveFile(requestPath)",
                ["operations"] = new JArray("inspect", "begin", "fork", "lock", "unlock", "status", "render", "preview", "complete", "fail", "cancel"),
                ["inlineShaderFX"] = true,
                ["lock"] = "whimtex_assistant_lock / WhimTexApi.LiveLock(requestId, layerId, sessionId, expectedRevision)",
                ["reference"] = "Documentation~/LiveAgentAPI.md" };
            result["independentLiveEditing"] = new JObject {
                ["command"] = "whimtex_headless_live / WhimTexApi.TiffLiveFile(requestPath)",
                ["operations"] = new JArray("begin", "list", "status", "preview", "render", "complete", "cancel"),
                ["windowRequired"] = false,
                ["notes"] = "In-memory TIFF session, lost on domain reload. Nonempty operations replay from the begin snapshot; omitted/empty operations keep the current working model. Complete checks the disk revision before saving. Only the last 32 successful complete/cancel receipts survive reload for bounded retry safety."
            };
            return result;
        });

        private static string Revision(WhimTexDocument document)
        {
            using var hash = SHA256.Create();
            foreach (Layer layer in Enumerate(document.layers))
                if (layer?.Behaviour is DrawingLayerBehaviour drawing) drawing.SyncPendingSurfaceToTexture();
            bool json = WhimTexDocumentJson.IsJsonPath(WhimTexDocumentService.PathOf(document));
            var text = new StringBuilder(json ? WhimTexDocumentJson.Write(document,
                new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full, AllowDrawingOmission = true }).Json
                : EditorJsonUtility.ToJson(document));
            foreach (Layer layer in Enumerate(document.layers))
            {
                if (layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture != null)
                {
                    Require(drawing.StoredTexture.isReadable, "Drawing texture is not readable.", "invalid_document");
                    if (json) text.Append("|drawing:").Append(layer.Id).Append(':').Append(drawing.StoredTexture.width)
                        .Append('x').Append(drawing.StoredTexture.height).Append(':').Append(drawing.StoredTexture.format)
                        .Append(':').Append(drawing.StoredTexture.mipmapCount).Append(':').Append(drawing.StoredTexture.isDataSRGB).Append(':');
                    text.Append(Convert.ToBase64String(hash.ComputeHash(drawing.StoredTexture.GetRawTextureData())));
                }
                if (!json && layer.fx != null)
                    foreach (UnityEngine.Object fxEntry in layer.fx)
                        if (fxEntry != null) text.Append(DocumentFxRevision(fxEntry));
            }
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private static string DocumentFxRevision(UnityEngine.Object fxEntry)
        {
            string json = EditorJsonUtility.ToJson(fxEntry);
            if (fxEntry is not ShaderFX) return json;
            // Opening/rendering an embedded FX rebuilds these derived fields. They are not
            // document edits and must not make Inspect disagree with a detached TIFF load.
            var model = JObject.Parse(json);
            var fields = model["MonoBehaviour"] as JObject ?? model;
            fields.Remove("compiledShader"); fields.Remove("appliedCode");
            fields.Remove("appliedSource"); fields.Remove("appliedParameters");
            fields.Remove("diagnostics"); fields.Remove("lastApplyFailed");
            fields.Remove("shaderCreationRecorded"); fields.Remove("embeddedOwner");
            // Newly discovered declarations receive UI identity IDs on load. API edits
            // address parameters by name; regenerated IDs do not change their meaning.
            if (fields["parameters"] is JArray parameters)
                foreach (JObject parameter in parameters) parameter.Remove("id");
            return model.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string DocumentAssetPath(WhimTexDocument document) =>
            WhimTexDocumentService.PathOf(document);

        private static JObject Snapshot(WhimTexDocument document, string path)
        {
            var layers = new JArray();
            Collect(document.layers, null);
            return new JObject
            {
                ["assetPath"] = path, ["guid"] = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path),
                ["revision"] = Revision(document), ["width"] = document.width, ["height"] = document.height,
                ["dirty"] = EditorUtility.IsDirty(document) || document.documentBinding?.dirty == true, ["hasOutputTexture"] = document.OutputTexture != null,
                ["layers"] = layers
            };

            void Collect(List<Layer> source, string parent)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    Layer layer = source[i];
                    if (layer == null) continue;
                    JObject settings = new JObject { ["name"] = layer.layerName, ["enabled"] = layer.enabled };
                    var entry = new JObject { ["id"] = layer.Id, ["type"] = TypeName(layer), ["parent"] = parent, ["index"] = i, ["settings"] = settings };
                    entry["behaviourMissing"] = layer.Behaviour == null;
                    entry["fx"] = LiveFxSnapshot(layer, document);
                    entry["contentRevision"] = LiveLayerRevision(layer);
                    entry["contentLocked"] = IsLayerContentLocked(document, layer);
                    if (layer?.Behaviour is PendingLayerBehaviour pending)
                    {
                        entry["jobId"] = pending.jobId;
                        entry["contentLocked"] = true;
                        entry["status"] = LiveReservationStatus(pending);
                    }
                    settings["opacity"] = layer.opacity;
                    settings["clippingMask"] = layer.clippingMask;
                    entry["clippingBaseId"] = document.GetClippingBase(layer)?.Id;
                    if (layer?.AsGroup() is Layer clippingGroup)
                        entry["isolatedByClipping"] = document.IsGroupIsolatedByClipping(clippingGroup);
                    settings["blend"] = layer.blendMode.ToString();
                    settings["colorRange"] = layer.colorRange.ToString();
                    settings["blendRange"] = layer.blendRange.ToString();
                    settings["channelMapping"] = new JArray(LayerChannelMapping.Labels[(int)layer.channelMapping[0]],
                        LayerChannelMapping.Labels[(int)layer.channelMapping[1]], LayerChannelMapping.Labels[(int)layer.channelMapping[2]],
                        LayerChannelMapping.Labels[(int)layer.channelMapping[3]]);
                    if (layer?.AsGroup() is Layer folder) settings["compositing"] = folder.compositing.ToString();
                    if (layer?.Behaviour is DrawingLayerBehaviour stored) entry["storageFormat"] = stored.StoredTexture != null ? stored.StoredTexture.format.ToString() : "Unallocated";
                    if (!layer.IsGroup)
                        settings["filter"] = layer.filterMode.ToString();
                    entry["transform"] = new JObject
                    {
                        ["pivot"] = new JArray(layer.transform.pivot.x, layer.transform.pivot.y), ["tiling"] = layer.transform.tiling.ToString()
                    };
                    var transformJson = (JObject)entry["transform"];
                    var t = layer.transform;
                    if (t.storage == TransformStorage.Projective)
                    {
                        var m = t.matrix;
                        transformJson["matrix"] = new JArray(m.m00, m.m01, m.m02, m.m10, m.m11, m.m12, m.m20, m.m21, m.m22);
                    }
                    else
                    {
                        transformJson["position"] = new JArray(t.position.x, t.position.y);
                        transformJson["scale"] = new JArray(t.scale.x, t.scale.y);
                        transformJson["rotation"] = t.rotation;
                    }
                    entry["fxCount"] = layer.fx?.Count ?? 0;
                    if (layer?.Behaviour is FileLayerBehaviour file)
                    {
                        settings["source"] = file.sourceTexture != null ? AssetDatabase.GetAssetPath(file.sourceTexture) : "";
                        if (file.sourceTexture != null)
                        {
                            var renderedSource = document.ResolveOriginalFileTexture(file.sourceTexture) ?? file.sourceTexture;
                            entry["sourceSize"] = new JArray(renderedSource.width, renderedSource.height);
                            entry["importedSourceSize"] = new JArray(file.sourceTexture.width, file.sourceTexture.height);
                        }
                    }
                    if (layer?.Behaviour is ColorFillLayerBehaviour fill)
                    {
                        settings["color"] = Json(fill.color);
                        settings["fillMode"] = fill.mode.ToString();
                        settings["fillPattern"] = FillPatternSnapshot(fill.pattern ?? new FillPatternSettings());
                    }
                    if (layer?.Behaviour is DrawingLayerBehaviour drawing) settings["brush"] = BrushSnapshot(drawing);
                    if (layer?.Behaviour is TargetedLayerBehaviour targeted)
                    {
                        entry["input"] = targeted.inputMode.ToString();
                        entry["target"] = targeted.TargetLayerId;
                        entry["inputValid"] = document.HasUsableEffectInput(targeted, source, i);
                    }
                    if (layer?.Behaviour is OutlineLayerBehaviour outline)
                    {
                        settings["color"] = Json(outline.outlineColor);
                        settings["metric"] = outline.metric.ToString();
                        settings["sourceChannel"] = outline.sourceChannel.ToString();
                        settings["outlineWidth"] = outline.outlineWidth;
                        settings["outlineSoftness"] = outline.outlineSoftness;
                        settings["outlinePosition"] = outline.outlinePosition.ToString();
                        settings["outlineOffset"] = outline.outlineOffset;
                        settings["fillCenter"] = outline.fillCenter;
                        settings["fillColor"] = Json(outline.fillColor);
                    }
                    if (layer?.Behaviour is SDFLayerBehaviour sdf)
                    {
                        settings["metric"] = sdf.metric.ToString();
                        settings["sourceChannel"] = sdf.sourceChannel.ToString();
                        settings["threshold"] = sdf.threshold;
                        settings["distancePosition"] = sdf.distancePosition.ToString();
                        settings["inverted"] = sdf.inverted;
                        settings["maxDistance"] = sdf.maxDistanceNormalization;
                        settings["sourceOffset"] = new JArray(sdf.sourceOffset.x, sdf.sourceOffset.y);
                        settings["sourceEdges"] = sdf.sourceEdges.ToString();
                        settings["contourOffset"] = sdf.contourOffset;
                        settings["insideDistance"] = sdf.insideDistance;
                        settings["outsideDistance"] = sdf.outsideDistance;
                        settings["profile"] = WhimTexCurveTexture.Format(sdf.profile);
                        settings["encoding"] = sdf.encoding.ToString();
                        entry["gradientKeys"] = GradientSnapshot(sdf.gradient);
                    }
                    if (layer?.Behaviour is NormalMapLayerBehaviour normal) settings["normalMap"] = NormalMapSnapshot(normal);
                    if (layer?.Behaviour is BlurLayerBehaviour blur) settings["blur"] = BlurSnapshot(blur);
                    if (layer?.Behaviour is SharpenLayerBehaviour sharpen) settings["sharpen"] = SharpenSnapshot(sharpen);
                    if (layer?.Behaviour is MakeSeamlessLayerBehaviour seamless) settings["makeSeamless"] = MakeSeamlessSnapshot(seamless);
                    if (layer?.Behaviour is NoiseLayerBehaviour noise) settings["noise"] = NoiseSnapshot(noise);
                    if (layer?.Behaviour is ShapeLayerBehaviour shape) settings["shape"] = ShapeSnapshot(shape);
                    if (layer?.Behaviour is TextLayerBehaviour text)
                    {
                        settings["text"] = TextSnapshot(text);
                        entry["fontAvailable"] = text.FontAvailable;
                        entry["resolvedFont"] = text.ResolvedFont;
                        if (text.Notice != null) entry["warnings"] = new JArray(text.Notice);
                    }
                    if (layer?.Behaviour is GradientLayerBehaviour gradient) entry["gradientKeys"] = GradientSnapshot(gradient.gradient);
                    layers.Add(entry);
                    if (layer?.AsGroup() is Layer group) Collect(group.layers, layer.Id);
                }
            }
        }

        private static JObject GradientSnapshot(WhimTexGradient gradient)
        {
            var colors = new JArray();
            var alphas = new JArray();
            if (gradient != null)
            {
                var colorKeys = gradient.ColorKeys;
                var alphaKeys = gradient.AlphaKeys;
                for (int i = 0; i < colorKeys.Length; i++) colors.Add(new JObject { ["time"] = colorKeys[i].time, ["color"] = Json(colorKeys[i].color), ["midpoint"] = i + 1 < colorKeys.Length ? gradient.GetMidpoint(false, i) : .5f });
                for (int i = 0; i < alphaKeys.Length; i++) alphas.Add(new JObject { ["time"] = alphaKeys[i].time, ["alpha"] = alphaKeys[i].alpha, ["midpoint"] = i + 1 < alphaKeys.Length ? gradient.GetMidpoint(true, i) : .5f });
            }
            return new JObject { ["colors"] = colors, ["alphas"] = alphas,
                ["mode"] = (gradient?.Mode ?? WhimTexGradientMode.Perceptual).ToString(),
                ["wrapMode"] = (gradient?.WrapMode ?? WhimTexGradientWrapMode.Clamp).ToString(),
                ["smoothness"] = gradient?.Smoothness ?? 1f,
                ["colorSpace"] = (gradient?.ColorSpace ?? ColorSpace.Gamma).ToString() };
        }

        private static JObject BrushSnapshot(DrawingLayerBehaviour layer)
        {
            BrushDynamics dynamics = layer.brushDynamics ?? new BrushDynamics();
            return new JObject
            {
                ["opacity"] = dynamics.opacity, ["flow"] = dynamics.flow, ["pressure"] = dynamics.pressure, ["scatter"] = dynamics.scatter,
                ["writeChannels"] = dynamics.writeChannels, ["lockAlpha"] = dynamics.lockAlpha,
                ["scatterBias"] = dynamics.scatterBias,
                ["sizeJitter"] = dynamics.sizeJitter,
                ["angleJitter"] = dynamics.angleJitter,
                ["flipX"] = dynamics.flipX, ["flipY"] = dynamics.flipY,
                ["angleOffset"] = dynamics.angleOffset,
                ["rotationMode"] = dynamics.rotationMode.ToString(),
                ["randomAlgorithm"] = dynamics.randomAlgorithm.ToString(),
                ["tintGradientKeys"] = GradientSnapshot(dynamics.tintGradient),
                ["tip"] = dynamics.tip != null ? AssetDatabase.GetAssetPath(dynamics.tip) : null,
                ["tipChannel"] = dynamics.tipChannel.ToString(), ["blend"] = dynamics.blend.ToString(), ["seed"] = dynamics.seed,
                ["tipSdf"] = dynamics.tipSdf, ["tipGradientKeys"] = GradientSnapshot(dynamics.tipGradient),
                ["proceduralMode"] = dynamics.proceduralMode.ToString(),
                ["blendApplication"] = dynamics.blendApplication.ToString(),
                ["color"] = Json(layer.brushColor), ["size"] = layer.brushSize, ["hardness"] = layer.brushHardness,
                ["spacing"] = layer.brushSpacing, ["mirrorX"] = layer.mirrorAcrossVerticalAxis, ["mirrorY"] = layer.mirrorAcrossHorizontalAxis,
                ["center"] = Json(layer.patternCenter), ["repeat"] = layer.repeatMode.ToString(), ["repeatCount"] = layer.repeatCount,
                ["radialStartAngle"] = layer.radialStartAngle,
                ["mirrorAngle"] = layer.mirrorAngle,
                ["repeatSecondaryCount"] = layer.repeatSecondaryCount, ["elements"] = layer.repeatElementMode.ToString(), ["boundary"] = layer.repeatBoundaryMode.ToString()
            };
        }

        private static string TypeName(Layer layer) =>
            layer?.Behaviour is PendingLayerBehaviour ? "pending" :
            LayerTypeRegistry.Find(layer?.Behaviour?.GetType())?.ApiId ??
            (layer?.IsGroup == true ? "group" : layer?.Behaviour?.GetType().Name ?? "missing");
    }
}
