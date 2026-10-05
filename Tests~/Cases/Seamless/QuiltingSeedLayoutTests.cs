using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class QuiltingSeedLayoutTests
{

    const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;

    static string Begin(WhimTex.Tests.UnityC.AsyncFixture job)
    {
        
        var focus=EditorWindow.focusedWindow;var window=job.Scope.Own(ScriptableObject.CreateInstance<EditorWindow>());
        var assembly=typeof(TextureCompositor).Assembly;
        var model=MakeSeamlessLayerBehaviour.CreateDefault();model.mode=MakeSeamlessLayerBehaviour.SeamlessMode.PatchQuilting;
        int step=0;float previousInput=0,smallInput=0;string report="";
        void Cleanup() => job.DisposeOwned();
        job.OwnCleanup(() => {job.Scope.CloseWindow(window);if(focus!=null)focus.Focus();});
        try
        {
            window.ShowUtility();window.position=new Rect(100,100,320,850);
            var root=window.rootVisualElement;
            assembly.GetType("DCFApixels.WhimTex.WhimTexUI").GetMethod("ApplyWindowStyles",F).Invoke(null,new object[]{root});
            var bindings=Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.WhimTexUI+ValueBindings"),true);
            void Refresh()=>bindings.GetType().GetMethod("Refresh",F).Invoke(bindings,new object[]{true});
            typeof(MakeSeamlessLayerEditorWindow).GetMethod("BuildFields",F).Invoke(null,new object[]{root,model,null,new Action<string,Action>((s,a)=>{a();Refresh();}),bindings,new Action<VisualElement,TargetedLayerBehaviour>((r,l)=>{})});
            var seed=root.Q<IntegerField>("quiltingSeed");var button=root.Q<Button>("quiltingRandomSeed");
            void Check(bool ok,string message){ job.Context.True(ok, message); }
            void Measure()
            {
                try
                {
                    var row=seed.parent;var input=seed.Q(className:"unity-base-field__input");
                    Check(row.worldBound.width>250,$"Laid out row: row={row.worldBound}, root={root.worldBound}, window={window.position}, parentDisplay={row.parent.resolvedStyle.display}");
                    Check(Math.Abs(button.worldBound.width-64)<1,"Fixed button width");
                    Check(Math.Abs(row.worldBound.xMax-button.worldBound.xMax-button.resolvedStyle.marginRight)<2,"Button at row end");
                    Check(button.worldBound.xMin-seed.worldBound.xMax<10&&button.worldBound.xMin>=seed.worldBound.xMax,"Field fills remaining row");
                    Check(input.worldBound.width>100,"Input not content-sized");
                    if(step%2==1)Check(Math.Abs(input.worldBound.width-previousInput)<1,"Seed content cannot change width");
                    if(step==2)Check(input.worldBound.width>smallInput+200,"Input expands with window");
                    report+=$"step {step}: row={row.worldBound.width:F0}, input={input.worldBound.width:F0}, button={button.worldBound.width:F0}; ";
                    previousInput=input.worldBound.width;
                    if(step==0)smallInput=previousInput;
                    step++;
                    if(step==4){job.Pass();Cleanup();return;}
                    if(step==2)window.position=new Rect(100,100,620,850);
                    seed.SetValueWithoutNotify(step%2==1?int.MinValue:0);
                    job.Schedule(root, Measure, 100);
                }
                catch(Exception e){job.Fail(e);Cleanup();}
            }
            job.Schedule(root, Measure, 100);
            return job.Read();
        }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }

    public static string Poll(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Poll(runId);
    public static string Result(string runId) => Poll(runId);
    public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cancel(runId);
    public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityC.AsyncFixture.Cleanup(runId);

    public static string Start(string runId)
    {
        var job = WhimTex.Tests.UnityC.AsyncFixture.Create(runId);
        try { return Begin(job); }
        catch (Exception error) { job.Fail(error); return job.Read(); }
    }
}
