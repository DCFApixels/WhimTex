using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum WhimTexLightType { Directional, Point, Spot, Area }
    public enum WhimTexAreaLightShape { Rectangle, Disk }

    [Serializable]
    public sealed class WhimTexLight
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Light";
        public bool enabled = true;
        public WhimTexLightType type;
        [ColorUsage(false, true)] public Color color = Color.white;
        public float intensity = 1;
        // Canvas pixels, bottom-left origin; Z is height above the canvas.
        // Directional: toward the source. Spot: emitted cone axis, away from the source.
        public Vector3 position = new Vector3(256, 256, 256);
        public Vector3 direction = new Vector3(-.5f, .5f, 1);
        public float range = 1024;
        public float falloff = 2;
        public float angle = 60;
        public float softness = .25f;
        public WhimTexAreaLightShape areaShape;
        public Vector2 size = new Vector2(256, 256);
        public float rotation;
        public Texture2D cookie;

        internal bool EmitsAlongDirection => type == WhimTexLightType.Spot || type == WhimTexLightType.Area;
        internal bool ActiveCookie => enabled && intensity > 0 && type == WhimTexLightType.Spot && cookie != null;
        internal bool ActiveArea => enabled && intensity > 0 && type == WhimTexLightType.Area;

        internal void Frame(out Vector3 right, out Vector3 up)
        {
            var frame = Quaternion.FromToRotation(Vector3.back, direction.normalized) * Quaternion.AngleAxis(rotation, Vector3.forward);
            right = frame * Vector3.right;
            up = frame * Vector3.up;
        }

        internal Vector3 DirectionForType(WhimTexLightType next) =>
            EmitsAlongDirection != (next == WhimTexLightType.Spot || next == WhimTexLightType.Area) ? -direction : direction;

        internal void Normalize()
        {
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
            name ??= "Light";
            if (!Enum.IsDefined(typeof(WhimTexLightType), type)) type = WhimTexLightType.Directional;
            for (int i = 0; i < 3; i++)
            {
                if (!float.IsFinite(position[i])) position[i] = 0;
                if (!float.IsFinite(direction[i])) direction[i] = 0;
                if (!float.IsFinite(color[i])) color[i] = 0;
            }
            if (direction.sqrMagnitude < 1e-12f) direction = EmitsAlongDirection ? Vector3.back : Vector3.forward;
            intensity = float.IsFinite(intensity) ? Mathf.Max(0, intensity) : 0;
            range = float.IsFinite(range) ? Mathf.Max(.001f, range) : 1;
            falloff = float.IsFinite(falloff) ? Mathf.Max(.01f, falloff) : 2;
            angle = float.IsFinite(angle) ? Mathf.Clamp(angle, 1, 179) : 60;
            softness = float.IsFinite(softness) ? Mathf.Clamp01(softness) : .25f;
            if (!Enum.IsDefined(typeof(WhimTexAreaLightShape), areaShape)) areaShape = WhimTexAreaLightShape.Rectangle;
            for (int i = 0; i < 2; i++) size[i] = float.IsFinite(size[i]) ? Mathf.Max(.001f, size[i]) : 256;
            rotation = float.IsFinite(rotation) ? Mathf.Repeat(rotation, 360) : 0;
        }
    }

    public sealed partial class WhimTexDocument
    {
        // Fixed, portable shader constants: no graphics-buffer dependency or global state.
        public const int MaximumLights = 64;
        public List<WhimTexLight> lights = new List<WhimTexLight>();
        [ColorUsage(false, true)] public Color ambientColor = Color.white;
        [NonSerialized] private Vector4[] lightPositions, lightColors, lightDirections, lightCones;
        [NonSerialized] private Vector4[] lightRight, lightUp;
        [NonSerialized] private LightCookieAtlas lightCookieAtlas;
        internal const string AreaLightKeyword = "WHIMTEX_AREA_LIGHTS";
        internal const string CookieLightKeyword = "WHIMTEX_LIGHT_COOKIES";
        static readonly int LightCountId = Shader.PropertyToID("_WhimTex_LightCount");
        static readonly int LightPositionsId = Shader.PropertyToID("_WhimTex_LightPositions");
        static readonly int LightColorsId = Shader.PropertyToID("_WhimTex_LightColors");
        static readonly int LightDirectionsId = Shader.PropertyToID("_WhimTex_LightDirections");
        static readonly int LightConesId = Shader.PropertyToID("_WhimTex_LightCones");
        static readonly int AmbientColorId = Shader.PropertyToID("_WhimTex_AmbientColor");
        static readonly int LightRightId = Shader.PropertyToID("_WhimTex_LightRight");
        static readonly int LightUpId = Shader.PropertyToID("_WhimTex_LightUp");

        public WhimTexLight AddLight(WhimTexLightType type)
        {
            NormalizeLights();
            if (lights.Count >= MaximumLights) throw new InvalidOperationException("A document supports up to 64 lights.");
            var light = new WhimTexLight { type = type, name = type + " " + (lights.Count + 1),
                position = new Vector3(width * .5f, height * .5f, Mathf.Max(width, height) * .25f),
                range = Mathf.Max(width, height) };
            if (light.EmitsAlongDirection) light.direction = Vector3.back;
            light.size = Vector2.one * Mathf.Max(width, height) * .25f;
            lights.Add(light);
            MarkChanged();
            return light;
        }

        public bool RemoveLight(string id)
        {
            if (lights == null) return false;
            int index = lights.FindIndex(light => light != null && light.id == id);
            if (index < 0) return false;
            WhimTexDocumentJson.RestoreMissingAssets(this);
            lights.RemoveAt(index);
            WhimTexDocumentJson.CaptureMissingAssets(this);
            MarkChanged();
            return true;
        }

        public void NotifyLightsChanged() => MarkChanged();

        private void NormalizeLights()
        {
            for (int i = 0; i < 3; i++)
                if (!float.IsFinite(ambientColor[i])) ambientColor[i] = 0;
            ambientColor.a = 1;
            lights ??= new List<WhimTexLight>();
            if (lights.Count > MaximumLights) throw new InvalidOperationException("A document supports up to 64 lights.");
            if (lights.Count == 0) return;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var light in lights)
            {
                if (light == null) throw new InvalidOperationException("A document light cannot be null.");
                light.Normalize();
                if (!ids.Add(light.id)) throw new InvalidOperationException("Document light IDs must be unique.");
            }
        }

        internal ulong LightingFingerprint
        {
            get
            {
                ulong hash = 14695981039346656037UL;
                for (int i = 0; i < 3; i++) hash = RenderFingerprint.Float(hash, ambientColor[i]);
                if (lights == null) return hash;
                foreach (var light in lights)
                {
                    if (light == null) continue;
                    hash = RenderFingerprint.Float(hash, light.enabled ? 1 : 0);
                    hash = RenderFingerprint.Float(hash, (float)light.type);
                    for (int i = 0; i < 3; i++)
                    {
                        hash = RenderFingerprint.Float(hash, light.position[i]);
                        hash = RenderFingerprint.Float(hash, light.direction[i]);
                        hash = RenderFingerprint.Float(hash, light.color[i]);
                    }
                    hash = RenderFingerprint.Float(hash, light.intensity);
                    hash = RenderFingerprint.Float(hash, light.range);
                    hash = RenderFingerprint.Float(hash, light.falloff);
                    hash = RenderFingerprint.Float(hash, light.angle);
                    hash = RenderFingerprint.Float(hash, light.softness);
                    if (light.type == WhimTexLightType.Area)
                    {
                        hash = RenderFingerprint.Float(hash, (float)light.areaShape);
                        hash = RenderFingerprint.Float(hash, light.size.x);
                        hash = RenderFingerprint.Float(hash, light.size.y);
                    }
                    if (light.EmitsAlongDirection) hash = RenderFingerprint.Float(hash, light.rotation);
                    if (light.type == WhimTexLightType.Spot) hash = LightCookieAtlas.TextureStamp(hash, light.cookie);
                }
                return hash;
            }
        }

        internal void BindLighting(Material material)
        {
            int count = lights?.Count ?? 0;
            if (count > MaximumLights) throw new InvalidOperationException("A document supports up to 64 lights.");
            material.SetInt(LightCountId, count);
            material.SetVector(AmbientColorId, new Vector4(ambientColor.r, ambientColor.g, ambientColor.b, 1));
            bool area = false, cookies = false;
            for (int i = 0; i < count; i++) { area |= lights[i].ActiveArea; cookies |= lights[i].ActiveCookie; }
            SetLightingKeyword(material, AreaLightKeyword, area);
            SetLightingKeyword(material, CookieLightKeyword, cookies);
            if (cookies)
            {
                lightCookieAtlas ??= new LightCookieAtlas();
                lightCookieAtlas.Bind(material, lights);
            }
            else if (lightCookieAtlas != null)
            {
                material.SetTexture("_WhimTex_LightCookieAtlas", null);
                ReleaseLightCookies();
            }
            if (area || cookies)
            {
                lightRight ??= new Vector4[MaximumLights];
                lightUp ??= new Vector4[MaximumLights];
            }
            if (count == 0) return;
            lightPositions ??= new Vector4[MaximumLights];
            lightColors ??= new Vector4[MaximumLights];
            lightDirections ??= new Vector4[MaximumLights];
            lightCones ??= new Vector4[MaximumLights];
            for (int i = 0; i < count; i++)
            {
                var light = lights[i];
                lightPositions[i] = new Vector4(light.position.x, light.position.y, light.position.z, (float)light.type);
                float strength = light.enabled ? light.intensity : 0;
                lightColors[i] = new Vector4(light.color.r * strength, light.color.g * strength, light.color.b * strength, light.falloff);
                Vector3 direction = light.direction.normalized;
                lightDirections[i] = new Vector4(direction.x, direction.y, direction.z, light.range);
                float halfAngle = Mathf.Clamp(light.angle, 1, 179) * .5f * Mathf.Deg2Rad;
                lightCones[i] = new Vector4(Mathf.Cos(halfAngle),
                    Mathf.Cos(halfAngle * (1 - Mathf.Clamp01(light.softness))), light.softness > 0 ? 1 : 0, (float)light.areaShape);
                if (area || cookies)
                {
                    light.Frame(out Vector3 right, out Vector3 up);
                    lightRight[i] = new Vector4(right.x, right.y, right.z, light.size.x * .5f);
                    lightUp[i] = new Vector4(up.x, up.y, up.z, (light.areaShape == WhimTexAreaLightShape.Disk ? light.size.x : light.size.y) * .5f);
                }
            }
            material.SetVectorArray(LightPositionsId, lightPositions);
            material.SetVectorArray(LightColorsId, lightColors);
            material.SetVectorArray(LightDirectionsId, lightDirections);
            material.SetVectorArray(LightConesId, lightCones);
            if (area || cookies)
            {
                material.SetVectorArray(LightRightId, lightRight);
                material.SetVectorArray(LightUpId, lightUp);
            }
        }

        static void SetLightingKeyword(Material material, string keyword, bool enabled)
        {
            if (material.IsKeywordEnabled(keyword) == enabled) return;
            if (enabled) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
        }

        private void ReleaseLightCookies() { lightCookieAtlas?.Dispose(); lightCookieAtlas = null; }
    }
}
