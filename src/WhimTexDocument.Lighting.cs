using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum WhimTexLightType { Directional, Point }

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
        // Direction points from the surface toward a directional source.
        public Vector3 position = new Vector3(256, 256, 256);
        public Vector3 direction = new Vector3(-.5f, .5f, 1);
        public float range = 1024;
        public float falloff = 2;

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
            if (direction.sqrMagnitude < 1e-12f) direction = Vector3.forward;
            intensity = float.IsFinite(intensity) ? Mathf.Max(0, intensity) : 0;
            range = float.IsFinite(range) ? Mathf.Max(.001f, range) : 1;
            falloff = float.IsFinite(falloff) ? Mathf.Max(.01f, falloff) : 2;
        }
    }

    public sealed partial class WhimTexDocument
    {
        // Fixed, portable shader constants: no graphics-buffer dependency or global state.
        public const int MaximumLights = 64;
        public List<WhimTexLight> lights = new List<WhimTexLight>();
        [NonSerialized] private Vector4[] lightPositions, lightColors, lightDirections;
        static readonly int LightCountId = Shader.PropertyToID("_WhimTex_LightCount");
        static readonly int LightPositionsId = Shader.PropertyToID("_WhimTex_LightPositions");
        static readonly int LightColorsId = Shader.PropertyToID("_WhimTex_LightColors");
        static readonly int LightDirectionsId = Shader.PropertyToID("_WhimTex_LightDirections");

        public WhimTexLight AddLight(WhimTexLightType type)
        {
            NormalizeLights();
            if (lights.Count >= MaximumLights) throw new InvalidOperationException("A document supports up to 64 lights.");
            var light = new WhimTexLight { type = type, name = type + " " + (lights.Count + 1),
                position = new Vector3(width * .5f, height * .5f, Mathf.Max(width, height) * .25f),
                range = Mathf.Max(width, height) };
            lights.Add(light);
            MarkChanged();
            return light;
        }

        public bool RemoveLight(string id)
        {
            if (lights == null) return false;
            int index = lights.FindIndex(light => light != null && light.id == id);
            if (index < 0) return false;
            lights.RemoveAt(index);
            MarkChanged();
            return true;
        }

        public void NotifyLightsChanged() => MarkChanged();

        private void NormalizeLights()
        {
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
                }
                return hash;
            }
        }

        internal void BindLighting(Material material)
        {
            int count = lights?.Count ?? 0;
            if (count > MaximumLights) throw new InvalidOperationException("A document supports up to 64 lights.");
            material.SetInt(LightCountId, count);
            if (count == 0) return;
            lightPositions ??= new Vector4[MaximumLights];
            lightColors ??= new Vector4[MaximumLights];
            lightDirections ??= new Vector4[MaximumLights];
            for (int i = 0; i < count; i++)
            {
                var light = lights[i];
                lightPositions[i] = new Vector4(light.position.x, light.position.y, light.position.z, (float)light.type);
                float strength = light.enabled ? light.intensity : 0;
                lightColors[i] = new Vector4(light.color.r * strength, light.color.g * strength, light.color.b * strength, light.falloff);
                Vector3 direction = light.direction.normalized;
                lightDirections[i] = new Vector4(direction.x, direction.y, direction.z, light.range);
            }
            material.SetVectorArray(LightPositionsId, lightPositions);
            material.SetVectorArray(LightColorsId, lightColors);
            material.SetVectorArray(LightDirectionsId, lightDirections);
        }
    }
}
