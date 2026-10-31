using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class NodePreview : Foldout, IDisposable
        {
            private const int Size = 128;
            private readonly ShaderGraphWindow window;
            private readonly Canvas canvas;
            private readonly DropdownField output;
            private readonly VisualElement surface;
            private readonly Image image;
            private readonly Label message;
            private ShaderGraphProgram program;
            private RenderTexture texture;
            private readonly string nodeId;
            private string compiledPort;
            private ulong fingerprint;
            private bool dirty = true, attempted, disposed;

            internal NodePreview(ShaderGraphWindow window, Canvas canvas, string nodeId)
            {
                this.window = window; this.canvas = canvas; this.nodeId = nodeId; text = "Preview"; value = true;
                AddToClassList("whimtex-shader-graph-preview");
                userData = nodeId;
                output = new DropdownField("Output", new List<string> { "Out" }, 0);
                output.RegisterValueChangedCallback(_ => dirty = true); Add(output);
                surface = new VisualElement(); surface.AddToClassList("whimtex-shader-graph-node-preview");
                surface.generateVisualContent += WhimTexGradientWindow.DrawCheckerboard;
                image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("whimtex-shader-graph-node-preview-image"); surface.Add(image); Add(surface);
                message = new Label(); message.AddToClassList("whimtex-shader-graph-preview-message"); Add(message);
                message.AddToClassList("whimtex-hidden");
                this.RegisterValueChangedCallback(e => { if (e.target == this && e.newValue) dirty = true; });
            }
            internal void Invalidate() => dirty = true;
            internal void RefreshOutputs()
            {
                var node = window.Graph?.FindNode(nodeId);
                var choices = node == null || node.kind == ShaderGraphNodeKind.Output ? new List<string> { "RGBA" } : new List<string>(ShaderGraphNodes.Outputs(node));
                if (node?.kind == ShaderGraphNodeKind.Split)
                    choices.RemoveAll(port => "XYZW".IndexOf(port, StringComparison.Ordinal) >= ShaderGraphNodes.Components(window.InputType(node, "In")));
                output.choices = choices;
                if (!choices.Contains(output.value)) output.SetValueWithoutNotify(choices[0]);
                output.EnableInClassList("whimtex-hidden", choices.Count <= 1);
                dirty = true;
            }
            internal bool Tick(ulong next, bool allowCompile)
            {
                if (disposed || !dirty || !CanDisplay()) return false;
                bool compile = !attempted || next != fingerprint || output.value != compiledPort;
                if (compile && !allowCompile) return false;
                dirty = false;
                var current = window.Graph?.FindNode(nodeId);
                if (current == null) { Release(); return false; }
                if (ShaderGraphNodes.Components(window.OutputType(current, output.value)) == 0)
                { message.text = "Connect a sampling node to preview this resource."; message.RemoveFromClassList("whimtex-hidden"); Release(); return false; }

                var previous = RenderTexture.active;
                bool srgb = GL.sRGBWrite;
                try
                {
                    if (compile)
                    {
                        program?.Dispose(); program = null;
                        attempted = true; fingerprint = next; compiledPort = output.value;
                        program = ShaderGraphProgram.Create(window.Graph, current.kind == ShaderGraphNodeKind.Output ? null : nodeId, output.value);
                    }
                    if (program == null) return compile;
                    texture ??= CreateTexture();
                    var context = new LayerRenderContext(window.document, null, Size, Size,
                        (float)Size / Mathf.Max(window.document.width, window.document.height), applyTransform: false, applyFx: false);
                    program.Bind(window.Graph, context);
                    ProceduralUv.Prepare(program.Material, window.Behaviour.Owner, context);
                    using var bindings = window.document.BindShaderTextureLayers(program.Parameters, window.Behaviour.Owner, context, null);
                    bindings?.Apply(program.Material);
                    GL.sRGBWrite = false;
                    Graphics.Blit(null, texture, program.Material, 0);
                    image.image = texture; message.text = string.Empty;
                }
                catch (Exception error) { image.image = null; message.text = error.Message; }
                finally
                {
                    message.EnableInClassList("whimtex-hidden", string.IsNullOrEmpty(message.text));
                    RenderTexture.active = previous; GL.sRGBWrite = srgb;
                }
                return compile;
            }
            private bool CanDisplay()
            {
                if (!value || panel == null || surface.contentRect.width < 1) return false;
                Rect bounds = surface.worldBound;
                for (VisualElement element = this; element != null; element = element.parent)
                {
                    if (element.resolvedStyle.display == DisplayStyle.None || element.resolvedStyle.visibility == Visibility.Hidden) return false;
                    if (element is ScrollView scroll && !scroll.contentViewport.worldBound.Overlaps(bounds)) return false;
                }
                return canvas.worldBound.Overlaps(bounds) && window.rootVisualElement.worldBound.Overlaps(bounds);
            }
            private static RenderTexture CreateTexture()
            {
                var result = ProfiledRenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                result.filterMode = FilterMode.Bilinear; result.wrapMode = TextureWrapMode.Clamp;
                return result;
            }
            private void Release()
            {
                image.image = null; program?.Dispose(); program = null;
                if (texture != null) ProfiledRenderTexture.ReleaseTemporary(texture);
                texture = null; attempted = false;
            }
            public void Dispose() { if (disposed) return; disposed = true; Release(); }
        }
    }
}
