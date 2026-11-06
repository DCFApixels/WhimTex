using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class LightHandleRenderingTests
{
    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);
    static async Task Execute(TestContext t, CancellationToken token)
    {
        var window = ScriptableObject.CreateInstance<EditorWindow>();
        var prior = EditorWindow.focusedWindow;
        var owners = new System.Collections.Generic.List<WhimTexWindow>();
        var documents = new System.Collections.Generic.List<WhimTexDocument>();
        var canvases = new System.Collections.Generic.List<VisualElement>();
        var iconCells = new System.Collections.Generic.List<VisualElement>();
        float iconOffset = 0;
        var f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        try
        {
            window.titleContent = new GUIContent("WhimTex light geometry preview");
            window.rootVisualElement.style.backgroundColor = new Color(.18f, .18f, .18f);
            var icons = new VisualElement(); icons.style.flexDirection = FlexDirection.Row; icons.style.height = 70;
            var drawIcon = typeof(WhimTexWindow).GetMethod("DrawLightIcon", BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 0; i < 16; i++)
            {
                int index = i;
                var cell = new VisualElement(); cell.style.width = i < 8 ? 90 : 45; cell.style.height = 70;
                cell.Add(new Label(((WhimTexLightType)(i % 8 / 2)).ToString().Substring(0, 3) + (i % 2 == 0 ? " / O" : " / F")));
                cell.generateVisualContent += context => drawIcon.Invoke(null, new object[] { context.painter2D,
                    (WhimTexLightType)(index % 8 / 2), new Vector2(index < 8 ? 45 : 22, 45) + Vector2.one * iconOffset,
                    new Color(1,.82f,.25f), index % 2 == 1, index < 8 ? 1f : .5f,
                    index < 8 ? 2f : 1f, index < 8 ? 1 / Mathf.Max(1,EditorGUIUtility.pixelsPerPoint) : 0f });
                icons.Add(cell); iconCells.Add(cell);
            }
            window.rootVisualElement.Add(icons);
            var names = new[] {"Spot / clipped cone", "Spot / grazing plane", "Spot / down", "Area / rectangle", "Area / tilted disk", "Point / range"};
            for (int row = 0; row < 2; row++)
            {
                var strip = new VisualElement(); strip.style.flexDirection = FlexDirection.Row; strip.style.height = 330;
                window.rootVisualElement.Add(strip);
                for (int col = 0; col < 3; col++)
                {
                    int index = row * 3 + col;
                    var cell = new VisualElement(); cell.style.width = 360; cell.style.height = 330;
                    var caption = new Label(names[index]);
                    caption.style.color = Color.white;
                    caption.style.unityTextAlign = TextAnchor.MiddleCenter; cell.Add(caption);
                    var owner = ScriptableObject.CreateInstance<WhimTexWindow>(); owners.Add(owner);
                    var doc = (WhimTexDocument)typeof(WhimTexWindow).GetField("activeDocument",f).GetValue(owner); documents.Add(doc);
                    doc.width=doc.height=512; doc.layers.Clear(); doc.lights.Clear();
                    var light = doc.AddLight(index<3?WhimTexLightType.Spot:index<5?WhimTexLightType.Area:WhimTexLightType.Point);
                    light.color=new Color(1,.65f,.1f); light.position=new Vector3(190,256,100); light.range=210;
                    light.direction=index==0?new Vector3(.8f,.15f,-.7f).normalized:index==1?new Vector3(1,0,-.1f).normalized:Vector3.back;
                    light.angle=index==1?100:95;light.softness=.3f;light.size=new Vector2(270,140);
                    if(index==4){light.areaShape=WhimTexAreaLightShape.Disk;light.direction=new Vector3(.5f,.2f,-1).normalized;}
                    var tool=typeof(WhimTexWindow).GetField("canvasTool",f);tool.SetValue(owner,Enum.Parse(tool.FieldType,"Lights"));
                    typeof(WhimTexWindow).GetMethod("NormalizeLightSelection",f).Invoke(owner,null);
                    var canvasType=typeof(WhimTexWindow).GetNestedType("CanvasElement",BindingFlags.NonPublic);
                    var canvas=(VisualElement)Activator.CreateInstance(canvasType,f,null,new[]{typeof(WhimTexWindow).GetField("canvasViewport",f).GetValue(owner)},null);
                    canvasType.GetField("documentWidth",f).SetValue(canvas,512);canvasType.GetField("documentHeight",f).SetValue(canvas,512);
                    typeof(WhimTexWindow).GetField("toolkitCanvas",f).SetValue(owner,canvas);
                    typeof(WhimTexWindow).GetMethod("BuildLightsCanvasTool",f).Invoke(owner,null);
                    canvas.style.width=360;canvas.style.height=300;canvas.style.overflow=Overflow.Hidden;
                    canvases.Add(canvas);cell.Add(canvas);
                    strip.Add(cell);
                }
            }
            window.ShowUtility(); window.position = new Rect(80, 80, 1080, 730); window.Repaint(); await Task.Delay(350,token);
            foreach(var canvas in canvases) canvas.GetType().GetProperty("ImageRect",f).SetValue(canvas,new Rect(0,0,360,300));
            foreach(var canvas in canvases) foreach(var child in canvas.Children())child.MarkDirtyRepaint();
            window.Repaint();await Task.Delay(150,token);
            if (window.rootVisualElement.panel == null) throw new Exception("Preview panel did not attach");
            string folder = Path.GetFullPath("Temp/WhimTex/light-handle-render/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = null;
            var hashes = new System.Collections.Generic.List<string>();
            for(int frame=0;frame<12;frame++)
            {
                {
                    token.ThrowIfCancellationRequested();
                    iconOffset = frame==1||frame==2?.25f:frame==3?.75f:0;
                    foreach(var cell in iconCells) cell.MarkDirtyRepaint();
                    float rotation=frame==1||frame==2?37:frame==3?90:0;
                    float zoom=frame==1||frame==2?1.3f:frame==3?2:1;
                    for(int index=0;index<owners.Count;index++)
                    {
                        var viewport=typeof(WhimTexWindow).GetField("canvasViewport",f).GetValue(owners[index]);
                        viewport.GetType().GetMethod("SetRotation",f).Invoke(viewport,new object[]{rotation,false});
                        canvases[index].GetType().GetProperty("ImageRect",f).SetValue(canvases[index],new Rect(frame==1||frame==2?.25f:0,frame==1||frame==2?.75f:0,360*zoom,300*zoom));
                        canvases[index].MarkDirtyRepaint();foreach(var child in canvases[index].Children())child.MarkDirtyRepaint();
                    }
                    path=Path.Combine(folder,"frame-"+frame+".png");
                    window.Repaint();await Task.Delay(150,token);
                }
                var probe = new CaptureProbe { owner = window, path = path, pickingMode = PickingMode.Ignore };
                probe.style.position = Position.Absolute; probe.style.width = probe.style.height = 1;
                window.rootVisualElement.Add(probe); window.Repaint();
                if (await Task.WhenAny(probe.done.Task, Task.Delay(10000)) != probe.done.Task) throw new TimeoutException("Icon frame capture timed out");
                await probe.done.Task;probe.RemoveFromHierarchy();
                using(var sha=System.Security.Cryptography.SHA256.Create()) hashes.Add(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))));
            }
            t.Equal(hashes[0], hashes[4], "Returning to the same view restores exactly the same pixels");
            t.Equal(hashes[4], hashes[5], "Repeated base frame is stable");
            t.Equal(hashes[1], hashes[2], "Repeated fractional/rotated frame is stable");
            for (int frame = 6; frame < hashes.Count; frame++)
                t.Equal(hashes[0], hashes[frame], "Repeated base frame remains stable: " + frame);
            t.True(hashes[0] != hashes[1] && hashes[1] != hashes[3], "View changes actually affect the rendered geometry");

        }
        finally { window.Close(); UnityEngine.Object.DestroyImmediate(window);foreach(var owner in owners)UnityEngine.Object.DestroyImmediate(owner);foreach(var doc in documents){Undo.ClearUndo(doc);UnityEngine.Object.DestroyImmediate(doc);} if (prior != null) prior.Focus(); }
    }
    sealed class CaptureProbe : ImmediateModeElement
    {
        internal EditorWindow owner;
        internal string path;
        internal TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
        protected override void ImmediateRepaint()
        {
            if (done.Task.IsCompleted) return;
            Texture2D image = null;
            try
            {
                var rt = RenderTexture.active;
                int w = rt != null ? rt.width : Mathf.RoundToInt(owner.position.width * EditorGUIUtility.pixelsPerPoint);
                int h = rt != null ? rt.height : Mathf.RoundToInt(owner.position.height * EditorGUIUtility.pixelsPerPoint);
                image = new Texture2D(w,h,TextureFormat.RGBA32,false);
                image.ReadPixels(new Rect(0,0,w,h),0,0,false); image.Apply(false);
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(image)); done.TrySetResult(true);
            }
            catch (Exception error) { done.TrySetException(error); }
            finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
