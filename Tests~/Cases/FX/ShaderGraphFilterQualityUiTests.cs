using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderGraphFilterQualityUiTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(o,args);
    public static async Task<object> Run()
    {
        var t = new TestContext(); ShaderGraphWindow window = null; WhimTexDocument document = null;
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 16;
            var shader = new ShaderLayerBehaviour(); document.layers.Add(shader); Call(document,"NormalizeModel");
            var graph = shader.draftGraph;
            var model = typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex.ShaderGraphNodes", true);
            var kinds = new[] { ShaderGraphNodeKind.Blur, ShaderGraphNodeKind.Sharpen, ShaderGraphNodeKind.Dilate, ShaderGraphNodeKind.Erode };
            var ids = new string[4];
            for(int i=0;i<4;i++)
            {
                var n = (ShaderGraphNode)model.GetMethod("Create",F).Invoke(null,new object[]{kinds[i],new Vector2(120+(i%2)*310,80+(i/2)*240)});
                n.previewExpanded=false; graph.nodes.Add(n); ids[i]=n.id;
            }
            window=ScriptableObject.CreateInstance<ShaderGraphWindow>(); window.titleContent=new GUIContent("Filter Quality Check");
            typeof(ShaderGraphWindow).GetField("document",F).SetValue(window,document);
            typeof(ShaderGraphWindow).GetField("layerId",F).SetValue(window,document.layers[0].Id);
            window.ShowUtility(); window.position=new Rect(60,80,1100,720); window.CreateGUI(); await Task.Delay(300);
            var canvas=(GraphView)typeof(ShaderGraphWindow).GetField("canvas",F).GetValue(window);
            Node View(int i)=>canvas.nodes.Single(n=>n.viewDataKey==ids[i]);
            DropdownField Quality(int i)=>View(i).Query<DropdownField>().ToList().Single(f=>f.label=="Quality");
            for(int i=0;i<4;i++)
            {
                var field=Quality(i);t.Equal("Standard",field.value,"Default quality "+kinds[i]);
                t.True(field.choices.SequenceEqual(new[]{"Standard","High"}),"Quality choices");
                t.True(field.labelElement.worldBound.width>0 && field.labelElement.worldBound.xMax<=field.worldBound.xMax+.5f,"Label fits field");
                field.value="High"; await Task.Delay(50);
                t.Equal(1,graph.FindNode(ids[i]).option,"UI updates quality");
                t.Equal("High",Quality(i).value,"Quality survives node rebuild");
            }
            View(1).Query<Toggle>().ToList().Single(f=>f.label=="Adaptive").value=true; await Task.Delay(100);
            t.True(!View(1).Query<DropdownField>().ToList().Any(f=>f.label=="Quality"),"Adaptive hides quality");
            t.Equal(1,graph.FindNode(ids[1]).option,"Adaptive retains quality");
            View(1).Query<Toggle>().ToList().Single(f=>f.label=="Adaptive").value=false; await Task.Delay(100);
            t.Equal("High",Quality(1).value,"Ordinary Sharpen restores quality");
            using var loaded=WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json,false);
            var restored=((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph;
            foreach(string id in ids)t.Equal(1,restored.FindNode(id).option,"Quality JSON roundtrip");
            return TestContext.Result("passed",t.Checks,"Four Quality controls, bounds, switching, Adaptive visibility and JSON persistence.");
        }
        finally
        {
            if(window!=null)window.Close();
            if(document!=null){Undo.ClearUndo(document);UnityEngine.Object.DestroyImmediate(document);}
        }
    }
}
