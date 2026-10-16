using System;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class FigureFillUiTests
{
    public sealed class TestWindow : EditorWindow { }
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    public static string Run()=>UnityBRun.Run("Shared Shape/Vector fill controls and Undo",()=>
    {
        var doc=UnityBRun.Create<WhimTexDocument>();doc.hideFlags=HideFlags.HideAndDontSave;
        var window=UnityBRun.Create<TestWindow>();window.titleContent=new GUIContent("Figure fill test");
        window.position=new Rect(100,100,420,300);window.ShowUtility();
        var assembly=typeof(WhimTexDocument).Assembly;
        var editorType=assembly.GetType("DCFApixels.WhimTex.FigureFillEditor",true);
        var bindingsType=assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings",true);
        foreach(bool isVector in new[]{false,true})
        foreach(bool stroke in new[]{false,true})
        {
            doc.layers.Clear();
            if(isVector) { var v=new VectorLayerBehaviour();v.shapes.Add(new VectorShape());doc.layers.Add(new Layer(v)); }
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
                var view=(VisualElement)Activator.CreateInstance(editorType,Flags,null,new object[]{Model(),change,bindings,stroke},null);
                window.rootVisualElement.Add(view);bindingsType.GetMethod("Refresh").Invoke(bindings,new object[]{true});
            }
            Build();var root=window.rootVisualElement;
            var names=new[]{"None","Solid","Linear","Radial","Square","Diamond","Circular"};
            for(int i=0;i<names.Length;i++)
            {
                root.Q<PopupField<string>>(prefix+"Type").value=names[i];
                UnityBRun.Check((int)Read<VectorFillMode>("fillMode")==i,"Selector writes shared enum "+names[i]);
                UnityBRun.Check(root.Q(prefix+"Color").ClassListContains("whimtex-hidden")== (i!=1),"Solid control visibility");
                UnityBRun.Check(root.Q(prefix+"Gradient").parent.ClassListContains("whimtex-hidden")== (i<2),"Gradient control visibility");
                if(i>=2) UnityBRun.Check(root.Q<Vector2Field>(prefix+"Start").label==(i==2?"Start":"Center"),"Coordinate labels");
            }
            var before=Read<Vector2>("gradientStart");var moved=new Vector2(.3f,.7f);
            root.Q<Vector2Field>(prefix+"Start").value=moved;
            UnityBRun.Check(Read<Vector2>("gradientStart")==moved,"Anchor change");
            Undo.PerformUndo();Build();UnityBRun.Check(Read<Vector2>("gradientStart")==before,"Undo restores anchor");
            UnityBRun.Check(root.Q<Vector2Field>(prefix+"Start").value==before,"Undo refreshes control");
            Undo.PerformRedo();Build();UnityBRun.Check(Read<Vector2>("gradientStart")==moved,"Redo restores anchor");
            var gradient=new WhimTexGradient { WrapMode=WhimTexGradientWrapMode.Mirror,Mode=WhimTexGradientMode.Fixed };
            gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.blue,1)},new[]{new GradientAlphaKey(.4f,0),new GradientAlphaKey(.8f,1)});
            ((INotifyValueChanged<WhimTexGradient>)root.Q(prefix+"Gradient")).value=gradient;
            UnityBRun.Check(Read<WhimTexGradient>("gradient").Equals(gradient),"Gradient field preserves all settings");
            UnityBRun.Check(!ReferenceEquals(Read<WhimTexGradient>("gradient"),gradient),"Gradient owns a copy");
            root.Q<PopupField<string>>(prefix+"Type").value="Solid";
            root.Q<PopupField<string>>(prefix+"Type").value="Circular";
            UnityBRun.Check(Read<WhimTexGradient>("gradient").Equals(gradient) && Read<Vector2>("gradientStart")==moved,"Inactive fill settings retained");
            root.Q<Vector2Field>(prefix+"End").value=new Vector2(float.NaN,1);
            UnityBRun.Check(float.IsFinite(Read<Vector2>("gradientEnd").x),"Invalid anchor rejected");
            Undo.ClearUndo(doc);
        }
    });
}
