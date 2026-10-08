// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class ShaderFXControlTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Run() => TestContext.Run("ShaderFXControlTests.Run", runContext =>
    {
        context = runContext;
        using (fixture = new MigrationD()) ExecuteMain();
    });

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static void ExecuteMain()
    {
        var assembly = typeof(ShaderFX).Assembly;
        var metadata = assembly.GetType("DCFApixels.WhimTex.ShaderFXMetadata");
        var parse = metadata.GetMethod("Parse", F);
        List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)parse.Invoke(null, new object[]{source,false,null});
        var reader = metadata.GetMethod("ReadControl", F);
        int checks = 0;
        void Check(bool value,string text) { context.True(value, text); }
        string Read(string source,out string warning)
        { object[] args={source,null};var name=(string)reader.Invoke(null,args);warning=(string)args[1];return name; }
        Check(Read("// @control(_A)",out var warning)=="_A" && warning=="", "First line control");
        Check(Read("// @whimtex-effect Color/Test\n// @control(_A)",out warning)=="_A" && warning=="", "Catalog position");
        Check(Read("// @control(_A)\n// @control(_B)",out warning)=="_B" && warning.Contains("last declaration wins"), "Last wins");
        Check(Read("/*\n// @control(_A)\n*/",out warning)==null && warning=="", "Ignore block comments");
        Check(Read("// @control(_A)\n// @control()",out warning)==null && warning.Contains("expected"), "Malformed last does not resurrect earlier binding");
        const string source="// @control(_Opacity)\n// @param label(Opacity) float _Opacity = 1 [0..1]\nfloat4 ApplyFX(float2 uv,float4 color){return color * _Opacity;}";
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();doc.width=16;doc.height=16;
        Layer layer=new ColorFillLayerBehaviour();doc.layers.Add(layer);
        ShaderFX effect=null;ShaderFXControlTestsWindow window=null;WhimTexDocument loaded=null;
        string folder=null;
        try
        {
            var draft=typeof(ShaderFX).GetMethod("CreateAgentDraft",F,null,new[]{typeof(WhimTexDocument),typeof(string),typeof(List<ShaderFXParameter>)},null);
            effect=(ShaderFX)draft.Invoke(null,new object[]{doc,source,Parse(source)});layer.fx.Add(effect);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(effect,null);
            var viewType=assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView");
            VisualElement View(bool header)=>(VisualElement)Activator.CreateInstance(viewType,F,null,new object[]{effect,header},null);
            void Refresh(VisualElement view)=>viewType.GetMethod("Refresh",F).Invoke(view,null);
            window=ScriptableObject.CreateInstance<ShaderFXControlTestsWindow>();window.ShowUtility();
            var header=View(true);var body=View(false);window.rootVisualElement.Add(header);window.rootVisualElement.Add(body);
            var number=header.Q<FloatField>();
            Check(number!=null&&number.label==string.Empty,"Unlabeled header field");
            Check(body.childCount>0,"Ordinary parameter remains in body");
            Undo.IncrementCurrentGroup();
            number.value=.25f;Undo.FlushUndoRecordObjects();Refresh(body);
            var parameters=(IReadOnlyList<ShaderFXParameter>)typeof(ShaderFX).GetProperty("Parameters",F).GetValue(effect);
            Check(parameters[0].floatValue==.25f,"Header edits actual parameter");
            Undo.PerformUndo();Refresh(header);Check(header.Q<FloatField>().value==1,"Undo refreshes header");
            Undo.PerformRedo();Refresh(header);Check(header.Q<FloatField>().value==.25f,"Redo refreshes header");
            var handle=header.Q(className:"whimtex-fx-effect-control-handle");
            Check(handle!=null&&handle.GetType().Name=="LayerActionIcon"&&handle.pickingMode==PickingMode.Position&&
                header.IndexOf(handle)<header.IndexOf(header.Q<FloatField>()),"Opacity uses an interactive alpha icon before the field");
            using(var evt=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=new Vector2(10,10)}))
            { evt.target=handle;handle.SendEvent(evt); }
            using(var evt=PointerMoveEvent.GetPooled(new Event{type=EventType.MouseDrag,button=0,mousePosition=new Vector2(60,10)}))
            { evt.target=handle;handle.SendEvent(evt); }
            using(var evt=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=new Vector2(60,10)}))
            { evt.target=handle;handle.SendEvent(evt); }
            Undo.FlushUndoRecordObjects();
            Check(Mathf.Approximately(header.Q<FloatField>().value,.5f),"Symbol drag changes shared numeric value");
            Check(!handle.HasPointerCapture(PointerId.mousePointerId),"Symbol releases pointer after drag");
            Undo.PerformUndo();Refresh(header);Check(Mathf.Approximately(header.Q<FloatField>().value,.25f),"Symbol drag is undoable");
            var writer=assembly.GetType("DCFApixels.WhimTex.ShaderFXPresetWriter");
            var portable=(string)writer.GetMethod("BuildSource",F).Invoke(null,new object[]{effect,"Clipboard Test"});
            Check(portable.StartsWith("// @whimtex-effect Clipboard Test\n// @control(_Opacity)"),"Preset export places directive second");
            Check(Read(portable,out warning)=="_Opacity"&&warning=="","Preset control survives");
            folder=fixture.AssetFolder();
            // GUID asset folder already created by fixture.
            loaded=WhimTexDocumentFile.Load(WhimTexDocumentFile.Save(doc,folder+"/control.tiff"));
            var restored=(ShaderFX)loaded.layers[0].fx[0];
            Check(Read((string)typeof(ShaderFX).GetProperty("Code",F).GetValue(restored),out warning)=="_Opacity","TIFF preserves binding");
            void Set(string code)
            {
                typeof(ShaderFX).GetField("code",F).SetValue(effect,code);
                typeof(ShaderFX).GetField("parameters",F).SetValue(effect,Parse(code));
                Refresh(header);Refresh(body);
            }
            Set(source.Replace("label(Opacity)","hidden label(Opacity)"));
            Check(header.Q<FloatField>()!=null&&body.childCount==0,"Hidden only suppresses body");
            foreach(var name in new[]{"_Opacity","_opacity","_ALPHA","_BaseAlpha","_AlphaStrength","_Amount"})
            {
                Set("// @control("+name+")\n// @param float "+name+" = 1");
                var icon=header.Q(className:"whimtex-fx-effect-control-handle");
                Check(name=="_Amount" ? icon is Label label && label.text=="↔" : icon.GetType().Name=="LayerActionIcon",
                    "Header handle follows parameter name: "+name);
            }
            foreach(var declaration in new[]{"bool _Value = true","enum _Value = A {A:0,B:1}","color _Value = #FF0000", "float2 _Value = (1,2)", "float3 _Value = (1,2,3)", "float4 _Value = (1,2,3,4)"})
            { Set("// @control(_Value)\n// @param "+declaration);Check(header.childCount==1,"Supported header type "+declaration); }
            Set("// @control(_Curve)\n// @param curve _Curve");
            Check(header.childCount==0&&body.childCount>0,"Unsupported type stays in body");
            Set("// @control(_Missing)\n"+source);
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(effect,null);
            Check(((string)typeof(ShaderFX).GetProperty("Diagnostics",F).GetValue(effect)).Contains("last declaration wins"),"Compilation succeeds with duplicate warning");
            Set("// @control(_Missing)\n// @param float _Value\nfloat4 ApplyFX(float2 uv,float4 color){return color;}");
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(effect,null);
            Check(header.childCount==0&&((string)typeof(ShaderFX).GetProperty("Diagnostics",F).GetValue(effect)).Contains("undeclared"),"Unknown reference is warning only");
            Set(source);
            var stackType=assembly.GetType("DCFApixels.WhimTex.LayerShaderFXView");
            var stack=(VisualElement)Activator.CreateInstance(stackType,F,null,new object[]{layer,doc,(Action<string,Action>)((label,action)=>action())},null);
            window.rootVisualElement.Add(stack);
            var main=stack.Q(className:"whimtex-fx-effect-control");
            var toolbar=main.parent;
            Check(toolbar.IndexOf(main)==toolbar.childCount-2,"Control immediately before menu");
            using(var evt=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=Vector2.one}))
            { evt.target=main.Q<FloatField>();main.Q<FloatField>().SendEvent(evt); }
            Check(!toolbar.HasPointerCapture(PointerId.mousePointerId),"Editing control does not start FX dragging");
            return;
        }
        finally
        {
            if(window!=null)window.Close();
            if(effect!=null) { Undo.ClearUndo(effect);UnityEngine.Object.DestroyImmediate(effect); }
            if(loaded!=null)UnityEngine.Object.DestroyImmediate(loaded);
            UnityEngine.Object.DestroyImmediate(doc);
            if(folder!=null)MigrationD.DeleteAsset(folder);
        }
    }
}
public sealed class ShaderFXControlTestsWindow : EditorWindow { }

