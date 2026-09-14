using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum PostFxSource { SceneView, GameCamera, Profile }
    public enum PostFxDepth { Solid, AlphaHeight, AlphaMask }
    public enum PostFxProjection { Source, Perspective, Orthographic }
    public enum PostFxBackground { SolidColor, Checkerboard }

    [Serializable]
    public sealed class PostFxPreviewSettings
    {
        public PostFxSource source;
        public Camera camera;
        public UnityEngine.Object profile;
        public PostFxProjection projection;
        public float fieldOfView = 60f;
        public float orthographicSize = 5f;
        public float near = .1f;
        public float far = 1000f;
        public PostFxDepth depth;
        public float distance = 10f;
        public float depthRange = 5f;
        public float threshold = .5f;
        public bool invert;
        public bool linkDistanceToZoom;
        public bool animate;
        public PostFxBackground backgroundMode;
    }

    public readonly struct PostFxPreviewRequest
    {
        public readonly PostFxPreviewSettings settings;
        public readonly RenderTexture input;
        public readonly Color background;
        public readonly float zoom;
        public readonly Color checkerLight, checkerDark;
        public readonly float checkerSize;
        public readonly Vector2 canvasSize;
        public PostFxPreviewRequest(PostFxPreviewSettings settings, RenderTexture input, Color background, float zoom, Vector2 canvasSize = default)
        {
            this.settings = settings; this.input = input; this.background = background; this.zoom = zoom;
            checkerLight = WhimTexUserSettings.CheckerLight;
            checkerDark = WhimTexUserSettings.CheckerDark;
            checkerSize = WhimTexUserSettings.CheckerSize;
            this.canvasSize = canvasSize.x > 0 && canvasSize.y > 0 ? canvasSize :
                input != null ? new Vector2(input.width, input.height) : Vector2.one;
        }
    }

    public abstract class PostFxPreviewBackend : IDisposable
    {
        private static readonly List<Func<PostFxPreviewBackend>> factories = new List<Func<PostFxPreviewBackend>>();
        public static void Register(Func<PostFxPreviewBackend> factory) { if (!factories.Contains(factory)) factories.Add(factory); }
        internal static PostFxPreviewBackend Create()
        {
            foreach (var factory in factories) { var backend = factory(); if (backend != null) return backend; }
            return null;
        }
        public abstract bool IsAvailable { get; }
        public abstract Type ProfileType { get; }
        public abstract int StateHash(PostFxPreviewSettings settings);
        public abstract string Render(in PostFxPreviewRequest request, RenderTexture destination);
        public abstract void Dispose();
    }
}
