using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DCFApixels.WhimTex
{
    internal sealed class UrpPostFxPreview : PostFxPreviewBackend
    {
        private Scene scene;
        private Camera camera;
        private UniversalAdditionalCameraData data;
        private Material material;
        private Mesh mesh;
        private VolumeStack stack;
        private static bool rendering;
        private static Camera[] cameraBuffer = Array.Empty<Camera>();
        public override bool IsAvailable => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset;
        public override Type ProfileType => typeof(VolumeProfile);

        [InitializeOnLoadMethod]
        private static void Install() => Register(() => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset ? new UrpPostFxPreview() : null);

        private static Camera Source(PostFxPreviewSettings settings) => settings.source == PostFxSource.SceneView
            ? SceneView.lastActiveSceneView?.camera : settings.source == PostFxSource.GameCamera ? settings.camera ?? Camera.main : null;

        private static void VolumeSource(Camera source, out LayerMask mask, out Transform trigger)
        {
            mask = 1;
            trigger = source != null ? source.transform : null;
            if (source == null) return;
            source.TryGetComponent<UniversalAdditionalCameraData>(out var sourceData);
            if (source.cameraType == CameraType.SceneView)
            {
                var main = Camera.main;
                sourceData = null;
                if (main != null) main.TryGetComponent(out sourceData);
                if (sourceData == null)
                {
                    int count = Camera.allCamerasCount;
                    if (cameraBuffer.Length < count) cameraBuffer = new Camera[count];
                    count = Camera.GetAllCameras(cameraBuffer);
                    for (int i = 0; i < count; i++)
                    {
                        var other = cameraBuffer[i];
                        if (other != null && other.cameraType == CameraType.Game &&
                            other.TryGetComponent(out sourceData)) break;
                    }
                    Array.Clear(cameraBuffer, 0, count);
                }
            }
            if (sourceData != null)
            {
                mask = sourceData.volumeLayerMask;
                if (sourceData.volumeTrigger != null) trigger = sourceData.volumeTrigger;
            }
        }

        public override int StateHash(PostFxPreviewSettings settings)
        {
            unchecked
            {
                int hash = JsonUtility.ToJson(settings).GetHashCode();
                Camera source = Source(settings);
                if (source != null)
                {
                    hash = hash * 31 + EditorJsonUtility.ToJson(source).GetHashCode();
                    hash = hash * 31 + source.transform.position.GetHashCode();
                    if (source.TryGetComponent<UniversalAdditionalCameraData>(out var ad))
                        hash = hash * 31 + EditorJsonUtility.ToJson(ad).GetHashCode();
                    hash = hash * 31 + CoreUtils.ArePostProcessesEnabled(source).GetHashCode();
                }
                hash = hash * 31 + (GraphicsSettings.currentRenderPipeline == null ? 0 :
                    EditorUtility.GetDirtyCount(GraphicsSettings.currentRenderPipeline));
                hash = RendererStateHash(source, hash);
                if (settings.source == PostFxSource.Profile) return ProfileHash(settings.profile as VolumeProfile, hash);
                VolumeSource(source, out var mask, out var trigger);
                hash = hash * 31 + mask.value;
                if (trigger != null) hash = hash * 31 + trigger.position.GetHashCode();
                foreach (Volume volume in VolumeManager.instance.GetVolumes(mask))
                {
                    if (volume == null) continue;
                    hash = hash * 31 + EditorJsonUtility.ToJson(volume).GetHashCode();
                    hash = hash * 31 + volume.transform.localToWorldMatrix.GetHashCode();
                    hash = ProfileHash(volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile, hash);
                }
                return hash;
            }
        }

        private static int ProfileHash(VolumeProfile profile, int hash)
        {
            if (profile == null) return hash;
            unchecked
            {
                foreach (var component in profile.components)
                    if (component != null) hash = hash * 31 + JsonUtility.ToJson(component).GetHashCode();
                return hash;
            }
        }

        private static int RendererStateHash(Camera source, int hash)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return hash;
            UniversalAdditionalCameraData sourceData = null;
            if (source != null) source.TryGetComponent(out sourceData);
            int index = sourceData == null ? -1 : RendererIndex(sourceData);
            using var serializedPipeline = new SerializedObject(pipeline);
            var renderers = serializedPipeline.FindProperty("m_RendererDataList");
            if (index < 0) index = serializedPipeline.FindProperty("m_DefaultRendererIndex")?.intValue ?? 0;
            if (renderers == null || index < 0 || index >= renderers.arraySize) return hash;
            var renderer = renderers.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
            if (renderer == null) return hash;
            unchecked
            {
                hash = hash * 31 + EditorJsonUtility.ToJson(renderer).GetHashCode();
                foreach (var feature in renderer.rendererFeatures)
                {
                    if (feature == null) continue;
                    hash = hash * 31 + EditorJsonUtility.ToJson(feature).GetHashCode();
                    if (!feature.isActive) continue;
                    using var serializedFeature = new SerializedObject(feature);
                    var property = serializedFeature.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is Material passMaterial)
                            hash = hash * 31 + EditorJsonUtility.ToJson(passMaterial).GetHashCode();
                }
                return hash;
            }
        }

        private void EnsureResources()
        {
            if (camera != null) return;
            Shader shader = Shader.Find("Hidden/TextureCompositor/URPPreviewSurface");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("URP preview surface shader is unavailable or unsupported.");
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject = new GameObject("WhimTex Post FX Camera") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.scene = scene;
                data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                mesh = new Mesh { name = "WhimTex Post FX Surface", hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(-1,1,0), new Vector3(1,1,0), new Vector3(1,-1,0) };
                mesh.uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) };
                mesh.triangles = new[] { 0,1,2,0,2,3 };
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
                var surface = new GameObject("WhimTex Post FX Surface") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(surface, scene);
                surface.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = surface.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderingLayerMask = 1u;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                stack = VolumeManager.instance.CreateStack();
            }
            catch { Dispose(); throw; }
        }

        public override string Render(in PostFxPreviewRequest request, RenderTexture destination)
        {
            if (!IsAvailable) throw new InvalidOperationException("The active render pipeline is not URP.");
            if (rendering) throw new InvalidOperationException("Recursive post-processing preview rendering is not supported.");
            var settings = request.settings;
            Camera source = Source(settings);
            if (settings.source != PostFxSource.Profile && source == null)
                throw new InvalidOperationException("Choose a Game Camera or open a Scene View.");
            if (settings.source == PostFxSource.Profile && !(settings.profile is VolumeProfile))
                throw new InvalidOperationException("Assign a URP Volume Profile.");
            if (((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).isStpUsed)
                throw new InvalidOperationException("STP requires temporal camera history and is not supported by the static Post FX surface.");
            EnsureResources();
            UniversalAdditionalCameraData sourceData = null;
            if (source != null)
            {
                camera.CopyFrom(source);
                source.TryGetComponent(out sourceData);
            }
            // Copy only owned values, never a source camera's stack, volume stack or temporal resources.
            data.antialiasing = sourceData != null ? sourceData.antialiasing : AntialiasingMode.None;
            data.antialiasingQuality = sourceData != null ? sourceData.antialiasingQuality : AntialiasingQuality.High;
            data.stopNaN = sourceData != null && sourceData.stopNaN;
            data.dithering = sourceData != null && sourceData.dithering;
            data.allowHDROutput = false;
            data.allowXRRendering = false;
            data.hideFlags = HideFlags.HideAndDontSave;
            data.SetRenderer(sourceData == null ? -1 : RendererIndex(sourceData));
            if (!(data.scriptableRenderer is UniversalRenderer))
                throw new InvalidOperationException("Post FX currently requires a URP Universal Renderer; the 2D renderer is not supported.");
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = settings.source == PostFxSource.Profile ||
                (settings.source == PostFxSource.SceneView ? CoreUtils.ArePostProcessesEnabled(source) : sourceData != null && sourceData.renderPostProcessing);
            data.renderShadows = false;
            // Consumers request Depth/Normal/Color through ConfigureInput; do not force camera copies.
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            string notice = "URP • opaque isolated surface with Renderer Features. Depth/Normals/Color maps are requested by consumers, not generated eagerly. Scene geometry and camera stacks are not rendered.";
            if (data.antialiasing == AntialiasingMode.TemporalAntiAliasing)
            {
                data.antialiasing = AntialiasingMode.None;
                notice += " Temporal AA skipped (no frame history).";
            }
            camera.enabled = false;
            camera.scene = scene;
            // CopyFrom also copies Scene View's stage mask, which excludes our preview scene.
            // Scope culling to the owned scene after every source switch (including Profile).
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.cameraType = CameraType.Game;
            camera.depthTextureMode = DepthTextureMode.None;
            camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            camera.rect = new Rect(0,0,1,1);
            camera.cullingMask = ~0;
            camera.useOcclusionCulling = false;
            camera.allowDynamicResolution = false;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(request.background.r, request.background.g, request.background.b, 1f);
            camera.aspect = (float)destination.width / destination.height;
            if (source == null || settings.projection != PostFxProjection.Source)
            {
                camera.usePhysicalProperties = false;
                camera.orthographic = settings.projection == PostFxProjection.Orthographic;
                camera.fieldOfView = Mathf.Clamp(Safe(settings.fieldOfView, 60), 1, 179);
                camera.orthographicSize = Mathf.Max(.001f, Safe(settings.orthographicSize, 5));
                camera.nearClipPlane = Mathf.Max(.001f, Safe(settings.near, .1f));
                camera.farClipPlane = Mathf.Max(camera.nearClipPlane + .1f, Safe(settings.far, 1000));
                camera.allowHDR = true;
            }
            camera.ResetProjectionMatrix();
            camera.ResetWorldToCameraMatrix();
            camera.ResetCullingMatrix();
            material.SetMatrix("_SurfaceInverseProjection", camera.projectionMatrix.inverse);
            material.SetMatrix("_SurfaceCameraToWorld", camera.cameraToWorldMatrix);
            float distance = Safe(settings.distance, 10) / (settings.linkDistanceToZoom ? Mathf.Max(.0001f, request.zoom) : 1f);
            material.SetTexture("_Source", request.input);
            material.SetVector("_DepthSettings", new Vector4((int)settings.depth, distance, Mathf.Max(0, Safe(settings.depthRange, 5)), Mathf.Clamp01(Safe(settings.threshold, .5f))));
            material.SetFloat("_Invert", settings.invert ? 1 : 0);
            material.SetColor("_Background", request.background.linear);
            material.SetColor("_CheckerLight", request.checkerLight.linear);
            material.SetColor("_CheckerDark", request.checkerDark.linear);
            material.SetVector("_CheckerSettings", new Vector4(request.canvasSize.x, request.canvasSize.y,
                Mathf.Max(1, Safe(request.checkerSize, 16)), settings.backgroundMode == PostFxBackground.Checkerboard ? 1 : 0));
            VolumeManager manager = VolumeManager.instance;
            VolumeStack previousStack = manager.stack;
            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var rendered = RenderTexture.GetTemporary(destination.width, destination.height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rendering = true;
            try
            {
                VolumeSource(source, out var mask, out var trigger);
                manager.Update(stack, settings.source == PostFxSource.Profile ? null : trigger,
                    settings.source == PostFxSource.Profile ? (LayerMask)0 : mask);
                if (settings.source == PostFxSource.Profile)
                    foreach (var component in ((VolumeProfile)settings.profile).components)
                    {
                        var state = component == null ? null : stack.GetComponent(component.GetType());
                        if (state != null && component.active) component.Override(state, 1f);
                    }
                // Depth/normal passes describe the alpha relief; motion history is not synthesized.
                var motion = stack.GetComponent<MotionBlur>();
                if (motion != null && motion.IsActive())
                {
                    motion.intensity.value = 0;
                    notice += " Motion Blur skipped (static surface).";
                }
                DisableTemporalOcclusion(stack, ref notice);
                manager.stack = stack;
                var renderRequest = new UniversalRenderPipeline.SingleCameraRequest { destination = rendered };
                if (!RenderPipeline.SupportsRenderRequest(camera, renderRequest))
                    throw new InvalidOperationException("The active URP renderer does not support isolated camera requests.");
                RenderPipeline.SubmitRenderRequest(camera, renderRequest);
                GL.sRGBWrite = false;
                Graphics.Blit(rendered, destination, material, 2);
                if (!data.renderPostProcessing) notice += " Post-processing is disabled on the source view/camera.";
                return notice;
            }
            finally
            {
                manager.stack = previousStack;
                camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgb;
                RenderTexture.ReleaseTemporary(rendered);
                rendering = false;
            }
        }

        private static void DisableTemporalOcclusion(VolumeStack ownedStack, ref string notice)
        {
            // Newer URP exposes GTAO through a Volume component; older 17.x has no such type.
            Type aoType = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusionVolumeOverride");
            if (aoType == null) return;
            var component = ownedStack.GetComponent(aoType);
            if (component == null) return;
            using var serialized = new SerializedObject(component);
            var temporal = serialized.FindProperty("m_TemporalFilter.m_Value");
            if (temporal == null || !temporal.boolValue) return;
            temporal.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            notice += " AO temporal filtering skipped (no motion history).";
        }

        private static float Safe(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        private static int RendererIndex(UniversalAdditionalCameraData source)
        {
            using var serialized = new SerializedObject(source);
            return serialized.FindProperty("m_RendererIndex")?.intValue ?? -1;
        }

        public override void Dispose()
        {
            if (stack != null) VolumeManager.instance.DestroyStack(stack);
            stack = null;
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            scene = default;
            if (material != null) Object.DestroyImmediate(material);
            if (mesh != null) Object.DestroyImmediate(mesh);
            material = null; mesh = null; camera = null; data = null;
        }
    }
}
