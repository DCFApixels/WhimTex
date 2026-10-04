using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
public static class BrushVertexTransportTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

private static string BodyRun()
{
// Transient GPU probe, no project compilation, asset imports or document changes.
var material = new UnityEngine.Material(UnityEngine.Shader.Find("Hidden/TextureCompositor/PaintBrush"));
var mesh = new UnityEngine.Mesh();
var target = UnityEngine.RenderTexture.GetTemporary(32, 32, 0, UnityEngine.RenderTextureFormat.ARGBHalf, UnityEngine.RenderTextureReadWrite.Linear);
var readback = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBAFloat, false, true);
var previous = UnityEngine.RenderTexture.active;
try
{
    material.EnableKeyword("BRUSH_TEXTURE");
    material.SetTexture("_BrushTip", UnityEngine.Texture2D.whiteTexture);
    material.SetVector("_TipAspect", UnityEngine.Vector4.one);
    // The current shader maps canvas vertices through these homogeneous rows.
    material.SetVector("_PaintRow0", new UnityEngine.Vector4(1,0,0,0));
    material.SetVector("_PaintRow1", new UnityEngine.Vector4(0,1,0,0));
    material.SetVector("_PaintRow2", new UnityEngine.Vector4(0,0,1,0));
    material.SetFloat("_SrcBlend", 1); material.SetFloat("_DstBlend", 0);
    var positions = new[] { new UnityEngine.Vector3(0,0,0), new UnityEngine.Vector3(0,1,0), new UnityEngine.Vector3(1,1,0), new UnityEngine.Vector3(1,0,0) };
    mesh.vertices = positions;
    mesh.uv = new[] { UnityEngine.Vector2.zero, UnityEngine.Vector2.up, UnityEngine.Vector2.one, UnityEngine.Vector2.right };
    for (int channel = 1; channel <= 6; channel++)
    {
        var data = new System.Collections.Generic.List<UnityEngine.Vector4>();
        for (int i = 0; i < 4; i++) data.Add(channel == 5 ? UnityEngine.Vector4.one :
            channel == 6 ? new UnityEngine.Vector4(32,1,0,1) : UnityEngine.Vector4.zero);
        mesh.SetUVs(channel, data);
    }
    mesh.SetIndices(new[] {0,1,2,3}, UnityEngine.MeshTopology.Quads, 0);
    float Draw(bool explicitMesh)
    {
        UnityEngine.RenderTexture.active = target;
        UnityEngine.GL.Clear(false, true, UnityEngine.Color.clear);
        UnityEngine.GL.PushMatrix();
        try
        {
            UnityEngine.GL.LoadOrtho();
            material.SetPass(0);
            if (explicitMesh)
            {
                var commands = new UnityEngine.Rendering.CommandBuffer();
                try
                {
                    commands.SetRenderTarget(target);
                    commands.SetViewProjectionMatrices(UnityEngine.Matrix4x4.identity, UnityEngine.Matrix4x4.Ortho(0,1,0,1,-1,1));
                    commands.DrawMesh(mesh, UnityEngine.Matrix4x4.identity, material, 0, 0);
                    UnityEngine.Graphics.ExecuteCommandBuffer(commands);
                }
                finally { commands.Release(); }
            }
            else
            {
                UnityEngine.GL.Begin(UnityEngine.GL.QUADS);
                for (int i = 0; i < 4; i++)
                {
                    UnityEngine.GL.MultiTexCoord2(0, positions[i].x, positions[i].y);
                    for (int channel = 1; channel <= 4; channel++) UnityEngine.GL.MultiTexCoord3(channel,0,0,0);
                    UnityEngine.GL.MultiTexCoord3(5,1,1,1);
                    UnityEngine.GL.MultiTexCoord(6, new UnityEngine.Vector3(32,1,0));
                    UnityEngine.GL.Vertex(positions[i]);
                }
                UnityEngine.GL.End();
            }
        }
        finally { UnityEngine.GL.PopMatrix(); }
        readback.ReadPixels(new UnityEngine.Rect(0,0,32,32),0,0);
        return readback.GetPixel(16,16).a;
    }
    float gl = Draw(false), explicitVertices = Draw(true);
    T.True(!(explicitVertices < .99f), "Explicit brush vertex transport lost stamp alpha.");
    return null;
}
finally
{
    UnityEngine.RenderTexture.active = previous;
    UnityEngine.RenderTexture.ReleaseTemporary(target);
    UnityEngine.Object.DestroyImmediate(readback);
    UnityEngine.Object.DestroyImmediate(mesh);
    UnityEngine.Object.DestroyImmediate(material);
}


}
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
}
