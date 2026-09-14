using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DCFApixels.WhimTex
{
    internal sealed class LiveOutputSession : IDisposable
    {
        private readonly Texture2D target;
        private readonly FilterMode savedFilter;
        private RenderTexture staging;
        private bool published;

        internal LiveOutputSession(Texture2D target)
        {
            if (target == null || !target.isReadable)
                throw new InvalidOperationException("Save the compositor first to create a readable output texture.");
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.RTToTexture) == 0)
                throw new InvalidOperationException("This graphics device does not support live texture updates.");
            if (target.format != TextureFormat.RGBA32 && target.format != TextureFormat.RGBAHalf &&
                target.format != TextureFormat.RGBAFloat)
                throw new InvalidOperationException("Save the compositor again to update its output texture format.");
            this.target = target;
            savedFilter = target.filterMode;
        }

        internal bool Matches(Texture2D texture) => target == texture;

        internal void SetFilter(FilterMode filter)
        {
            if (target != null) target.filterMode = filter;
        }

        internal void Publish(RenderTexture source)
        {
            if (source == null || target == null) return;
            if (!target.isDataSRGB && target.mipmapCount == 1 && source.width == target.width &&
                source.height == target.height && source.graphicsFormat == target.graphicsFormat)
            {
                published = true;
                Graphics.CopyTexture(source, 0, 0, target, 0, 0);
                SceneView.RepaintAll();
                EditorApplication.RepaintProjectWindow();
                return;
            }
            if (staging == null || !staging.IsCreated() || staging.width != target.width ||
                staging.height != target.height || staging.graphicsFormat != target.graphicsFormat ||
                staging.mipmapCount != target.mipmapCount)
            {
                ReleaseStaging();
                var descriptor = new RenderTextureDescriptor(target.width, target.height)
                {
                    graphicsFormat = target.graphicsFormat,
                    depthBufferBits = 0,
                    msaaSamples = 1,
                    useMipMap = target.mipmapCount > 1,
                    autoGenerateMips = false,
                    mipCount = target.mipmapCount
                };
                staging = new RenderTexture(descriptor)
                {
                    name = "WhimTex Live Output",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = target.filterMode,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!staging.Create() || staging.graphicsFormat != target.graphicsFormat)
                    throw new InvalidOperationException("The output texture format is not supported for live rendering on this device.");
            }

            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                Material conversion = WhimTexMaterials.Hdr;
                conversion.SetFloat("_Saturate", GraphicsFormatUtility.IsHDRFormat(target.graphicsFormat) ? 0f : 1f);
                conversion.SetFloat("_Encode", target.isDataSRGB ? 1f : 0f);
                conversion.SetFloat("_UseSwizzle", 0f);
                GL.sRGBWrite = false;
                Graphics.Blit(source, staging, conversion, 0);
                if (staging.useMipMap) staging.GenerateMips();
                published = true;
                for (int mip = 0; mip < target.mipmapCount; mip++)
                    Graphics.CopyTexture(staging, 0, mip, target, 0, mip);
            }
            finally
            {
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.active = previous;
            }
            SceneView.RepaintAll();
            EditorApplication.RepaintProjectWindow();
        }

        public void Dispose()
        {
            try
            {
                if (published && target != null && target.isReadable)
                    target.Apply(false, false);
            }
            finally
            {
                if (target != null) target.filterMode = savedFilter;
                published = false;
                ReleaseStaging();
                SceneView.RepaintAll();
                EditorApplication.RepaintProjectWindow();
            }
        }

        private void ReleaseStaging()
        {
            if (staging == null) return;
            staging.Release();
            UnityEngine.Object.DestroyImmediate(staging);
            staging = null;
        }
    }
}
