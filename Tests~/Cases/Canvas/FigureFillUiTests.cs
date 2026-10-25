using System;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class FigureFillUiTests
{
    public sealed class TestWindow : EditorWindow { }
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    public static string Start(string id) => UnityBRun.Start(id, "Shared Shape/Vector/Text fill icons and Undo", Execute);
    public static string Poll(string id) => UnityBRun.Poll(id);
    public static Task<string> Cancel(string id) => UnityBRun.Cancel(id);
    public static string Cleanup(string id) => UnityBRun.Cleanup(id);
    static void Click(Button button)
    {
        using var evt = NavigationSubmitEvent.GetPooled(); evt.target = button; button.SendEvent(evt);
    }
    sealed class Capture : ImmediateModeElement
    {
        internal EditorWindow owner; internal string path; internal bool completed;
        protected override void ImmediateRepaint()
        {
            if (completed) return;
            var rt = RenderTexture.active; float ppi = EditorGUIUtility.pixelsPerPoint;
            var image = new Texture2D(rt != null ? rt.width : Mathf.RoundToInt(owner.position.width * ppi),
                rt != null ? rt.height : Mathf.RoundToInt(owner.position.height * ppi), TextureFormat.RGBA32, false);
            try { image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0, false); image.Apply(false);
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG()); completed = true; }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
    }
    static async Task Execute()
    {
        var doc=UnityBRun.Create<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;
        var window=UnityBRun.Create<TestWindow>();window.titleContent=new GUIContent("Figure fill test");
        window.position=new Rect(100,100,420,520);window.ShowUtility();
        var assembly=typeof(WhimTexDocument).Assembly;
        var editorType=assembly.GetType("DCFApixels.WhimTex.FigureFillEditor",true);
        assembly.GetType("DCFApixels.WhimTex.WhimTexUI",true).GetMethod("ApplyWindowStyles",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[]{window.rootVisualElement});
        var bindingsType=assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings",true);
        foreach(string kind in new[]{"shape","vector","text"})
        foreach(bool stroke in kind=="text" ? new[]{false} : new[]{false,true})
        {
            bool isVector=kind=="vector", isText=kind=="text";
            doc.layers.Clear();
            if(isText) doc.layers.Add(new Layer(new TextLayerBehaviour()));
            else if(isVector) { var v=new VectorLayerBehaviour();v.shapes.Add(new VectorShape());doc.layers.Add(new Layer(v)); }
            else doc.layers.Add(new Layer(new ShapeLayerBehaviour()));
            object Model()=>isVector?(object)((VectorLayerBehaviour)doc.layers[0].Behaviour).shapes[0]:doc.layers[0].Behaviour;
            string Field(string field) => !stroke ? field : field == "fillMode" ? "strokeMode" : field == "fillColor" ? "strokeColor" : "stroke" + char.ToUpperInvariant(field[0]) + field.Substring(1);
            string prefix=stroke?"figureStroke":"figureFill";
            T Read<T>(string field)=>(T)Model().GetType().GetField(Field(field)).GetValue(Model());
            void Build()
            {
                window.rootVisualElement.Clear();var bindings=Activator.CreateInstance(bindingsType,true);
                Action<string,Action> change=(name,action)=>
                {
                    Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(doc,name);action();
                    bindingsType.GetMethod("Refresh").Invoke(bindings,new object[]{true});
                };
                var view=(VisualElement)Activator.CreateInstance(editorType,Flags,null,isText ? new object[]{Model(),change,bindings} : new object[]{Model(),change,bindings,stroke,doc},null);
                window.rootVisualElement.Add(view);bindingsType.GetMethod("Refresh").Invoke(bindings,new object[]{true});
            }
            Build();var root=window.rootVisualElement;
            var type=root.Q<BaseField<VectorFillMode>>(prefix+"Type");
            UnityBRun.Check(type != null && root.Q<PopupField<string>>(prefix+"Type") == null,"Fill type is an icon row");
            var names=new[]{"None","Solid","Linear","Radial","Angular","Square","Freeform"};
            var modes=new[]{VectorFillMode.None,VectorFillMode.Solid,VectorFillMode.LinearGradient,VectorFillMode.RadialGradient,VectorFillMode.AngularGradient,VectorFillMode.SquareGradient,VectorFillMode.Freeform};
            if(isText) { Array.Resize(ref names,6); Array.Resize(ref modes,6); }
            UnityBRun.Check(type.Query<Button>().ToList().Count == names.Length,"Consistent supported icon list for "+kind);
            if(isText) UnityBRun.Check(type.Q<Button>("gradientTypeFreeform")==null,"Text does not offer unsupported Freeform");
            for(int i=0;i<names.Length;i++)
            {
                var button=type.Q<Button>("gradientType"+names[i]);
                UnityBRun.Check(button.tooltip==names[i],"Icon tooltip names the type");
                Click(button);
                int selected=0; type.Query<Button>().ForEach(b=>
                {
                    if(b.ClassListContains("whimtex-tool-button--selected"))
                    { selected++; UnityBRun.Check(b==button,"Only the chosen icon is highlighted"); }
                });
                UnityBRun.Check(selected==1,"One highlighted type");
                UnityBRun.Check(Read<VectorFillMode>("fillMode")==modes[i],"Selector writes shared enum "+names[i]);
                UnityBRun.Check(root.Q(prefix+"Color").ClassListContains("whimtex-hidden")== (i!=1),"Solid control visibility");
                UnityBRun.Check(root.Q(prefix+"Gradient").parent.ClassListContains("whimtex-hidden")== (i<2 || i==6),"Gradient control visibility");
                if(i>=2 && i<6) UnityBRun.Check(root.Q<Vector2Field>(prefix+"Start").label==(i==2?"Start":"Center"),"Coordinate labels");
            }
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); Build();
            UnityBRun.Check(Read<VectorFillMode>("fillMode")==modes[modes.Length-2],"Undo restores the previous icon type");
            UnityBRun.Check(root.Q<BaseField<VectorFillMode>>(prefix+"Type").Q<Button>("gradientType"+names[names.Length-2]).ClassListContains("whimtex-tool-button--selected"),"Undo refreshes selection highlight");
            Undo.PerformRedo(); Build();
            UnityBRun.Check(Read<VectorFillMode>("fillMode")==modes[modes.Length-1],"Redo restores the selected type");
            Click(root.Q<BaseField<VectorFillMode>>(prefix+"Type").Q<Button>("gradientTypeSquare"));
            var before=Read<Vector2>("gradientStart");var moved=new Vector2(.3f,.7f);
            root.Q<Vector2Field>(prefix+"Start").value=moved;
            UnityBRun.Check(Read<Vector2>("gradientStart")==moved,"Anchor change");
            Undo.PerformUndo();Build();UnityBRun.Check(Read<Vector2>("gradientStart")==before,"Undo restores anchor");
            UnityBRun.Check(root.Q<Vector2Field>(prefix+"Start").value==before,"Undo refreshes control");
            Undo.PerformRedo();Build();UnityBRun.Check(Read<Vector2>("gradientStart")==moved,"Redo restores anchor");
            var gradient=new WhimTexGradient { WrapMode=WhimTexGradientWrapMode.Mirror,Interpolation=WhimTexGradientInterpolation.Fixed };
            gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.blue,1)},new[]{new GradientAlphaKey(.4f,0),new GradientAlphaKey(.8f,1)});
            ((INotifyValueChanged<WhimTexGradient>)root.Q(prefix+"Gradient")).value=gradient;
            UnityBRun.Check(Read<WhimTexGradient>("gradient").Equals(gradient),"Gradient field preserves all settings");
            UnityBRun.Check(!ReferenceEquals(Read<WhimTexGradient>("gradient"),gradient),"Gradient owns a copy");
            Click(root.Q<BaseField<VectorFillMode>>(prefix+"Type").Q<Button>("gradientTypeSolid"));
            Click(root.Q<BaseField<VectorFillMode>>(prefix+"Type").Q<Button>("gradientTypeAngular"));
            UnityBRun.Check(Read<WhimTexGradient>("gradient").Equals(gradient) && Read<Vector2>("gradientStart")==moved,"Inactive fill settings retained");
            root.Q<Vector2Field>(prefix+"End").value=new Vector2(float.NaN,1);
            UnityBRun.Check(float.IsFinite(Read<Vector2>("gradientEnd").x),"Invalid anchor rejected");
            type=root.Q<BaseField<VectorFillMode>>(prefix+"Type");
            foreach(int width in new[]{420,320})
            {
                window.position=new Rect(100,100,width,520);
                for(int tick=0;tick<4;tick++) { window.Repaint(); await UnityBRun.NextUpdate(); }
                var first=type.Q<Button>("gradientTypeNone");var last=type.Q<Button>("gradientType"+names[names.Length-1]);
                UnityBRun.Check(Mathf.Abs(first.worldBound.y-last.worldBound.y)<.01f,"Icons stay in one row at "+width);
                UnityBRun.Check(last.worldBound.xMax<=root.worldBound.xMax+.01f,"All supported icons fit at "+width);
                UnityBRun.Check(Mathf.Abs(first.resolvedStyle.width-24)<.01f,"Same button width as Gradient layer");
                UnityBRun.Check(Mathf.Abs(first.resolvedStyle.height-20)<.01f,"Same button height as other actions");
            }
            var capture=new Capture {owner=window,path="Temp/WhimTex/figure-fill-icons-"+Guid.NewGuid().ToString("N")+"-"+kind+"-"+(stroke?"stroke":"fill")+".png",pickingMode=PickingMode.Ignore};
            capture.style.position=Position.Absolute;capture.style.width=1;capture.style.height=1;root.Add(capture);
            for(int tick=0;tick<30 && !capture.completed;tick++) { window.Repaint(); await UnityBRun.NextUpdate(); }
            UnityBRun.Check(capture.completed,"Actual icon row captured");capture.RemoveFromHierarchy();
            Undo.ClearUndo(doc);
        }
    }
}
