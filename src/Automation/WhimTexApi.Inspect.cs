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
            TextureCompositor document = Load(path);
            try
            {
                Require(!TextureCompositorWindow.IsDocumentBusyForApi(document), "Finish the current paint/transform gesture first.", "document_busy");
                JObject result = Success();
                result["document"] = Snapshot(document, path);
                return result;
            }
            finally { ReleaseTransientDocument(document); }
        });

        public static string Describe() => Respond(() =>
        {
            JObject result = Success();
            result["operations"] = new JArray("add", "set", "transform", "target", "move", "stroke", "compact");
            result["storageFormats"] = new JArray("asset", "tiff");
            result["backends"] = new JObject {
                ["asset"] = "legacy Unity ScriptableObject compositor; readable and editable for compatibility",
                ["tiff"] = "window-independent WhimTexDocumentBuild; transient model with atomic TIFF save"
            };
            result["migration"] = "Use WhimTexApi.Migrate(sourcePath, destinationPath, overwrite) or whimtex_migrate. The legacy .asset remains unchanged.";
            result["diagnostics"] = new JObject {
                ["storage"] = "whimtex_inspect_storage / WhimTexApi.InspectStorage(assetPath): metadata-only TIFF block inspection",
                ["validate"] = "whimtex_validate / WhimTexApi.Validate(assetPath, render): structure, limits, references and Shader FX",
                ["status"] = "whimtex_status / WhimTexApi.Status(assetPath): disk revision, importer, dirty, lock and staged recovery state"
            };
            result["colorRanges"] = new JArray(System.Enum.GetNames(typeof(LayerColorRange)));
            result["blendRanges"] = new JArray(System.Enum.GetNames(typeof(LayerBlendRange)));
            result["swizzleChannels"] = new JArray(LayerSwizzle.Labels);
            result["clippingMask"] = "Boolean setting on every layer type. Clips to the first non-clipping sibling below; missing/hidden bases hide the chain. Participating groups are isolated; base alpha and opacity are preserved.";
            result["groupCompositing"] = new JArray(System.Enum.GetNames(typeof(GroupCompositing)));
            var layerTypes = new JArray();
            foreach (var descriptor in LayerTypeRegistry.Entries) layerTypes.Add(descriptor.ApiId);
            result["layerTypes"] = layerTypes;
            result["blurDefaults"] = BlurSnapshot(new BlurLayerBehaviour());
            result["blurModes"] = new JArray(System.Enum.GetNames(typeof(BlurType)));
            result["noiseDimensions"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.NoiseDimensions)));
            result["noiseDefaults"] = NoiseSnapshot(new NoiseLayerBehaviour());
            result["shapeDefaults"] = ShapeSnapshot(new ShapeLayerBehaviour());
            result["shapeKinds"] = new JArray(System.Enum.GetNames(typeof(ShapeLayerBehaviour.ShapeKind)));
            result["noiseTypes"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.NoiseType)));
            result["noiseWhiteColors"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.WhiteNoiseColor)));
            result["noiseFractals"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.FractalType)));
            result["noiseCellularDistances"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.CellularDistance)));
            result["noiseCellularReturns"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.CellularReturn)));
            result["noiseWarps"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.WarpType)));
            result["noiseEncodings"] = new JArray(System.Enum.GetNames(typeof(NoiseLayerBehaviour.OutputEncoding)));
            result["normalMapDefaults"] = NormalMapSnapshot(new NormalMapLayerBehaviour());
            result["makeSeamlessDefaults"] = MakeSeamlessSnapshot(new MakeSeamlessLayerBehaviour());
            result["makeSeamlessHorizontal"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.HorizontalDirection)));
            result["makeSeamlessVertical"] = new JArray(System.Enum.GetNames(typeof(MakeSeamlessLayerBehaviour.VerticalDirection)));
            result["blurDirections"] = new JArray(System.Enum.GetNames(typeof(BlurLayerBehaviour.MotionDirection)));
            result["blurEdges"] = new JArray(System.Enum.GetNames(typeof(BlurLayerBehaviour.EdgeMode)));
            result["blendModes"] = new JArray(System.Enum.GetNames(typeof(BlendMode)));
            result["tilingModes"] = new JArray(System.Enum.GetNames(typeof(TransformTilingMode)));
            result["filterModes"] = new JArray(System.Enum.GetNames(typeof(LayerFilterMode)));
            result["distanceMetrics"] = new JArray(System.Enum.GetNames(typeof(DistanceMetric)));
            result["repeatModes"] = new JArray(System.Enum.GetNames(typeof(PaintRepeatMode)));
            result["repeatElements"] = new JArray(System.Enum.GetNames(typeof(PaintRepeatElementMode)));
            result["repeatBoundaries"] = new JArray(System.Enum.GetNames(typeof(PaintRepeatBoundaryMode)));
            result["pencilShapes"] = new JArray(System.Enum.GetNames(typeof(PencilShape)));
            result["coordinates"] = "Layer index 0 is topmost. Transform position uses canvas pixels, +X right, +Y up; rotation is counterclockwise degrees. Pivot is bottom-left UV. canvasPixels stroke points use top-left origin; layerUv uses bottom-left UV.";
            result["limits"] = new JObject { ["requestBytes"] = 4194304, ["operations"] = 256, ["canvasPixels"] = MaxCanvasPixels,
                ["layers"] = 1024, ["drawingPixels"] = 67108864, ["strokePoints"] = 4096, ["strokeStamps"] = 100000, ["strokeCoveragePixels"] = 250000000 };
            result["editing"] = "Inspect before editing; expectedRevision is mandatory on existing documents. Use @aliases within a batch. New documents require save=true. dryRun validates without drawing or saving. Save failure may leave partial asset I/O: inspect before retrying.";
            result["reference"] = "Documentation~/AgentAPI.md";
            result["liveEditing"] = new JObject {
                ["fastBegin"] = "whimtex_begin / WhimTexApi.LiveBegin(requestId, name, source, area, sessionId, sourceLayerId, selectionMode, padding)",
                ["selectionModes"] = new JArray("strict", "guide"),
                ["sessions"] = "whimtex_sessions / WhimTexApi.LiveSessions()",
                ["execute"] = "whimtex_live / WhimTexApi.LiveFile(requestPath)",
                ["operations"] = new JArray("inspect", "begin", "fork", "lock", "unlock", "status", "render", "preview", "complete", "fail", "cancel"),
                ["inlineShaderFX"] = true,
                ["lock"] = "whimtex_lock / WhimTexApi.LiveLock(requestId, layerId, sessionId, expectedRevision)",
                ["reference"] = "Documentation~/LiveAgentAPI.md" };
            result["independentLiveEditing"] = new JObject {
                ["command"] = "whimtex_tiff_live / WhimTexApi.TiffLiveFile(requestPath)",
                ["operations"] = new JArray("begin", "status", "preview", "render", "complete", "cancel"),
                ["windowRequired"] = false,
                ["notes"] = "Persistent transient TIFF session. Preview replaces the working model from the begin snapshot; complete performs one atomic save after disk revision validation."
            };
            return result;
        });

        private static string Revision(TextureCompositor document)
        {
            using var hash = SHA256.Create();
            var text = new StringBuilder(EditorJsonUtility.ToJson(document));
            string path = AssetDatabase.GetAssetPath(document);
            if (!string.IsNullOrEmpty(path)) text.Append(AssetDatabase.GetAssetDependencyHash(path));
            foreach (Layer layer in Enumerate(document.layers))
            {
                if (layer?.Behaviour is DrawingLayerBehaviour drawing && drawing.StoredTexture != null)
                {
                    Require(drawing.StoredTexture.isReadable, "Drawing texture is not readable.", "invalid_document");
                    text.Append(Convert.ToBase64String(hash.ComputeHash(drawing.StoredTexture.GetRawTextureData())));
                }
                if (layer.modifiers != null)
                    foreach (UnityEngine.Object modifier in layer.modifiers)
                        if (modifier != null) text.Append(EditorJsonUtility.ToJson(modifier));
            }
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private static JObject Snapshot(TextureCompositor document, string path)
        {
            var layers = new JArray();
            Collect(document.layers, null);
            return new JObject
            {
                ["assetPath"] = path, ["guid"] = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path),
                ["revision"] = Revision(document), ["width"] = document.width, ["height"] = document.height,
                ["dirty"] = EditorUtility.IsDirty(document), ["hasOutputTexture"] = document.OutputTexture != null,
                ["hasOutputSprite"] = document.OutputSprite != null, ["layers"] = layers
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
                    settings["swizzle"] = new JArray(LayerSwizzle.Labels[(int)layer.swizzle[0]],
                        LayerSwizzle.Labels[(int)layer.swizzle[1]], LayerSwizzle.Labels[(int)layer.swizzle[2]],
                        LayerSwizzle.Labels[(int)layer.swizzle[3]]);
                    if (layer?.AsGroup() is Layer folder) settings["compositing"] = folder.compositing.ToString();
                    if (layer?.Behaviour is DrawingLayerBehaviour stored) entry["storageFormat"] = stored.StoredTexture != null ? stored.StoredTexture.format.ToString() : "Unallocated";
                    if (!layer.IsGroup)
                    {
                        settings["opacity"] = layer.opacity;
                        settings["blend"] = layer.blendMode.ToString();
                        settings["filter"] = layer.filterMode.ToString();
                        entry["transform"] = new JObject
                        {
                            ["pivot"] = new JArray(layer.transform.pivot.x,layer.transform.pivot.y), ["tiling"] = layer.transform.tiling.ToString()
                        };
                        var transformJson=(JObject)entry["transform"];
                        var t=layer.transform;
                        if(t.storage==TransformStorage.Projective)
                        {
                            var m=t.matrix;
                            transformJson["matrix"]=new JArray(m.m00,m.m01,m.m02,m.m10,m.m11,m.m12,m.m20,m.m21,m.m22);
                        }
                        else
                        {
                            transformJson["position"]=new JArray(t.position.x,t.position.y);
                            transformJson["scale"]=new JArray(t.scale.x,t.scale.y);
                            transformJson["rotation"]=t.rotation;
                        }
                        entry["modifierCount"] = layer.modifiers?.Count ?? 0;
                    }
                    if (layer?.Behaviour is FileLayerBehaviour file)
                    {
                        settings["source"] = file.sourceTexture != null ? AssetDatabase.GetAssetPath(file.sourceTexture) : "";
                        if (file.sourceTexture != null)
                            entry["sourceSize"] = new JArray(file.sourceTexture.width, file.sourceTexture.height);
                    }
                    if (layer?.Behaviour is ColorFillLayerBehaviour fill) settings["color"] = Json(fill.color);
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
                        entry["gradientKeys"] = GradientSnapshot(sdf.gradient);
                    }
                    if (layer?.Behaviour is NormalMapLayerBehaviour normal) settings["normalMap"] = NormalMapSnapshot(normal);
                    if (layer?.Behaviour is BlurLayerBehaviour blur) settings["blur"] = BlurSnapshot(blur);
                    if (layer?.Behaviour is MakeSeamlessLayerBehaviour seamless) settings["makeSeamless"] = MakeSeamlessSnapshot(seamless);
                    if (layer?.Behaviour is NoiseLayerBehaviour noise) settings["noise"] = NoiseSnapshot(noise);
                    if (layer?.Behaviour is ShapeLayerBehaviour shape) settings["shape"] = ShapeSnapshot(shape);
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
                ["mode"] = (gradient?.Mode ?? WhimTexGradientMode.Classic).ToString(),
                ["smoothness"] = gradient?.Smoothness ?? 1f,
                ["colorSpace"] = (gradient?.ColorSpace ?? ColorSpace.Gamma).ToString() };
        }

        private static JObject BrushSnapshot(DrawingLayerBehaviour layer)
        {
            BrushDynamics dynamics = layer.brushDynamics ?? new BrushDynamics();
            return new JObject
            {
                ["opacity"] = dynamics.opacity, ["flow"] = dynamics.flow, ["scatter"] = dynamics.scatter,
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
