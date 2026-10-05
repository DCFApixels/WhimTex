using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class GradientHistoryTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
    const string Name="Gradient history smoke";
    static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
    static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
    static void Call(object o,string n,params object[] a)=>o.GetType().GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(o,a);
    static void Check(bool ok,string message){UnityBRun.Check(!(!ok), message);}
    static WhimTexGradientWindow Window()=>Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Single(w=>w.name==Name);
    static string ExecutePickerRecency()
    {
        if(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0)throw new UnityBSkipException("BLOCKED: close user picker first.");
        var doc=UnityBRun.Create<TextureCompositor>(); WhimTexColorPicker picker=null;
        try
        {
            Call(doc,"RememberColor",Color.red);Call(doc,"RememberColor",Color.green);
            var list=(List<Color>)Get(doc,"colorHistory");
            picker=(WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open",F).Invoke(null,new object[]{Color.blue,false,true,WhimTexColorRange.Switchable,doc,(Action<Color>)(_=>{}),null,null});
            Call(picker,"SetColor",Color.red,true);Check(list[0]==Color.green,"Intermediate match does not reorder");
            picker.Close();picker=null;Check(list.Count==2&&list[0]==Color.red,"Confirmed matching color moves first");
            picker=(WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open",F).Invoke(null,new object[]{Color.blue,false,true,WhimTexColorRange.Switchable,doc,(Action<Color>)(_=>{}),null,null});
            Call(picker,"SetColor",Color.green,true);Call(picker,"Finish",false);picker=null;
            Check(list.Count==2&&list[0]==Color.red,"Canceled match does not reorder");
            return "";
        }
        finally {if(picker!=null)UnityBRun.CloseOwned(picker);Undo.ClearUndo(doc);UnityEngine.Object.DestroyImmediate(doc);}
    }
    [Serializable] public sealed class CaptureJournal
    {
        public string runId, state, path, error, focusName, workflowToken;
        public string window, session, document, focus;
        public double queuedAt;
    }
    static string Identity(UnityEngine.Object value)
    {
        if(value==null)return null;
#if UNITY_6000_4_OR_NEWER
        return value.GetEntityId().ToString();
#else
        return value.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
#endif
    }
    static string CaptureKey(string runId)
    {
        if(!Guid.TryParseExact(runId,"N",out _))throw new ArgumentException("Persistent N-format owned GUID required.");
        return "WhimTex.Tests.GradientCapture."+runId;
    }
    static string CaptureName(CaptureJournal journal,string part)=>"WhimTex.GradientCapture."+journal.runId+"."+part;
    static void SaveCapture(CaptureJournal journal)=>SessionState.SetString(CaptureKey(journal.runId),JsonUtility.ToJson(journal));
    static CaptureJournal ReadCapture(string runId)
    {
        string json=SessionState.GetString(CaptureKey(runId),"");
        if(json.Length==0)throw new InvalidOperationException("Setup(same GUID) must precede capture or cleanup.");
        var journal=JsonUtility.FromJson<CaptureJournal>(json);
        if(journal.runId!=runId)throw new InvalidOperationException("Capture journal GUID mismatch.");
        return journal;
    }
    static T CaptureOwned<T>(CaptureJournal journal,string id,string part) where T:UnityEngine.Object
    {
        if(string.IsNullOrEmpty(id))return null;
        foreach(var value in Resources.FindObjectsOfTypeAll<T>())if(Identity(value)==id)
        {
            if(value.name!=CaptureName(journal,part)||AssetDatabase.Contains(value))throw new InvalidOperationException("Capture journal no longer identifies an owned "+part);
            return value;
        }
        return null;
    }
    public static string Setup(string runId) => SetupOwned(runId, null);
    static string SetupOwned(string runId, string workflowToken)
    {
        string key=CaptureKey(runId);
        if(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0||Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Length!=0)
            return WhimTex.Tests.TestContext.Result("skipped",0,"Original prerequisite: close user picker/gradient windows first; no pre-existing window acquired.").ToJson();
        return WhimTex.Tests.TestContext.Run("GradientHistorySmoke.Setup",context=>
        {
            context.True(SessionState.GetString(key,"").Length==0,"No existing GUID journal is overwritten");
            var journal=new CaptureJournal{runId=runId,state="prepared",workflowToken=workflowToken,focus=Identity(EditorWindow.focusedWindow),focusName=EditorWindow.focusedWindow==null?null:EditorWindow.focusedWindow.name};
            SaveCapture(journal);
            if(workflowToken!=null)SessionState.SetString(WorkflowKey(runId),workflowToken);
            var doc=ScriptableObject.CreateInstance<TextureCompositor>();doc.name=CaptureName(journal,"document");journal.document=Identity(doc);SaveCapture(journal);
            for(int i=0;i<40;i++)Call(doc,"RememberColor",Color.HSVToRGB(i/40f,.7f,.8f));
            Call(doc,"RememberColor",new Color(4,2,.5f,.2f));
            var type=typeof(WhimTexGradientWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientSession");
            var session=ScriptableObject.CreateInstance(type);session.name=CaptureName(journal,"session");journal.session=Identity(session);SaveCapture(journal);Set(session,"document",doc);
            WhimTexGradientWindow w=null;
            try{w=WhimTexGradientWindow.Open(session,"gradient");}
            finally
            {
                foreach(var candidate in Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>())if(ReferenceEquals(Get(candidate,"owner"),session))
                {candidate.name=CaptureName(journal,"window");journal.window=Identity(candidate);SaveCapture(journal);}
            }
            context.True(w!=null,"New owned gradient capture window exists");
            w.position=new Rect(100,100,560,420);Call(w,"ToggleHdr");
            context.True(((List<Color>)Get(doc,"colorHistory")).Count==41,"Original 40 HSV colors plus HDR capture input retained");
        });
    }
    public static string Capture(string runId)
    {
        try
        {
            var journal=ReadCapture(runId);
            var w=CaptureOwned<WhimTexGradientWindow>(journal,journal.window,"window");
            if(w==null||w.rootVisualElement.panel==null)return WhimTex.Tests.TestContext.Result("skipped",0,"Owned gradient utility window must be visible and laid out for supported Unity repaint capture.").ToJson();
            if(journal.state!="prepared")throw new InvalidOperationException("Capture already queued/completed; use a new GUID for another capture.");
            string folder=System.IO.Path.GetFullPath("Temp/WhimTex/diagnostics/"+runId+"/gradient-history");
            if(System.IO.File.Exists(folder)||System.IO.Directory.Exists(folder))throw new System.IO.IOException("Capture evidence GUID already exists; never overwrite it.");
            for(string parent=System.IO.Path.GetDirectoryName(folder);parent!=null;parent=System.IO.Path.GetDirectoryName(parent))
                if((System.IO.File.Exists(parent)||System.IO.Directory.Exists(parent))&&(System.IO.File.GetAttributes(parent)&System.IO.FileAttributes.ReparsePoint)!=0)
                    throw new System.IO.IOException("Capture evidence cannot traverse a link.");
            System.IO.Directory.CreateDirectory(folder);journal.path=System.IO.Path.Combine(folder,"gradient-history.png");
            journal.state="running";journal.queuedAt=EditorApplication.timeSinceStartup;SaveCapture(journal);
            var probe=new CaptureProbe(w,runId){name="history-capture-"+runId,pickingMode=PickingMode.Ignore};
            probe.style.position=Position.Absolute;probe.style.left=0;probe.style.top=0;probe.style.width=1;probe.style.height=1;
            w.rootVisualElement.Add(probe);w.Repaint();return PollCapture(runId);
        }
        catch(Exception error){return WhimTex.Tests.TestContext.Result("failed",0,"Capture request failed",error.ToString()).ToJson();}
    }
    sealed class CaptureProbe:ImmediateModeElement
    {
        readonly WhimTexGradientWindow window;readonly string runId;bool done;
        internal CaptureProbe(WhimTexGradientWindow window,string runId){this.window=window;this.runId=runId;}
        protected override void ImmediateRepaint()
        {
            if(done)return;done=true;
            if(SessionState.GetString(CaptureKey(runId),"").Length==0)return;
            var journal=ReadCapture(runId);
            if(journal.state!="running")return;
            Texture2D image=null;var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
            try
            {
                if(CaptureOwned<WhimTexGradientWindow>(journal,journal.window,"window")!=window)throw new InvalidOperationException("Repaint window no longer belongs to capture GUID.");
                int width=active!=null?active.width:Mathf.RoundToInt(window.position.width*EditorGUIUtility.pixelsPerPoint);
                int height=active!=null?active.height:Mathf.RoundToInt(window.position.height*EditorGUIUtility.pixelsPerPoint);
                if(width<=0||height<=0)throw new InvalidOperationException("Original repaint target has no capture dimensions.");
                image=new Texture2D(width,height,TextureFormat.RGBA32,false);
                // Same public Unity framebuffer read as Legacy; no desktop/OS capture API.
                image.ReadPixels(new Rect(0,0,image.width,image.height),0,0,false);image.Apply(false);
                byte[] png=image.EncodeToPNG();
                if(png==null||png.Length==0)throw new InvalidOperationException("Unity repaint capture produced no PNG evidence.");
                System.IO.File.WriteAllBytes(journal.path,png);journal.state="passed";
            }
            catch(Exception error){journal.state="failed";journal.error=error.ToString();}
            finally{RenderTexture.active=active;GL.sRGBWrite=srgb;if(image!=null)UnityEngine.Object.DestroyImmediate(image);SaveCapture(journal);}
        }
    }
    public static string PollCapture(string runId)
    {
        try
        {
            var journal=ReadCapture(runId);
            if(journal.state=="running"&&EditorApplication.timeSinceStartup-journal.queuedAt>30)
            {journal.state="failed";journal.error="No supported Unity repaint capture completed within 30s; graphical repaint prerequisite unverified.";SaveCapture(journal);}
            if(journal.state=="passed")return WhimTex.Tests.TestContext.Result("passed",1,"Original repaint capture PNG retained: "+journal.path).ToJson();
            if(journal.state=="failed")return WhimTex.Tests.TestContext.Result("failed",0,"Repaint capture failed; no visual golden manufactured",journal.error).ToJson();
            return WhimTex.Tests.TestContext.Result("running",0,"Capture state: "+journal.state).ToJson();
        }
        catch(Exception error){return WhimTex.Tests.TestContext.Result("failed",0,"Capture poll failed",error.ToString()).ToJson();}
    }
    public static string Cleanup(string runId)=>WhimTex.Tests.TestContext.Run("GradientHistorySmoke.Cleanup",context=>
    {
        var journal=ReadCapture(runId);
        var window=CaptureOwned<WhimTexGradientWindow>(journal,journal.window,"window");
        var session=CaptureOwned<ScriptableObject>(journal,journal.session,"session");
        var document=CaptureOwned<TextureCompositor>(journal,journal.document,"document");
        var errors=new List<Exception>();
        foreach(UnityEngine.Object value in new UnityEngine.Object[]{window,session,document})if(value!=null)
            try{if(value is EditorWindow w)UnityBRun.CloseOwned(w);else{Undo.ClearUndo(value);UnityEngine.Object.DestroyImmediate(value);}}catch(Exception error){errors.Add(error);}
        if(errors.Count!=0)throw new AggregateException("Capture cleanup failed; GUID journal retained",errors);
        context.True(CaptureOwned<WhimTexGradientWindow>(journal,journal.window,"window")==null,"Owned capture window closed; PNG evidence retained");
        foreach(var w in Resources.FindObjectsOfTypeAll<EditorWindow>())if(Identity(w)==journal.focus&&w.name==journal.focusName){w.Focus();break;}
        SessionState.EraseString(CaptureKey(runId));
    });
    static async System.Threading.Tasks.Task ExecuteCaptureWorkflow(string runId)
    {
        if(SessionState.GetString(CaptureKey(runId),"").Length!=0)throw new InvalidOperationException("GUID already owns a manual capture; use a new GUID.");
        string workflowToken=Guid.NewGuid().ToString("N");
        var errors=new List<Exception>();
        try
        {
            var setup=JsonUtility.FromJson<WhimTex.Tests.TestResult>(SetupOwned(runId,workflowToken));
            if(setup.status=="skipped")throw new UnityBSkipException(setup.message);
            UnityBRun.Check(setup.status=="passed","Original capture setup: "+setup.ToJson());
            await UnityBRun.NextUpdate();await UnityBRun.Delay(250);
            var result=JsonUtility.FromJson<WhimTex.Tests.TestResult>(Capture(runId));
            if(result.status=="skipped")throw new UnityBSkipException(result.message);
            while(result.status=="running"){await UnityBRun.Delay(20);result=JsonUtility.FromJson<WhimTex.Tests.TestResult>(PollCapture(runId));}
            UnityBRun.Check(result.status=="passed","Supported Unity repaint capture completed: "+result.ToJson());
        }
        catch(Exception error){errors.Add(error);}
        finally
        {
            if(SessionState.GetString(WorkflowKey(runId),"")==workflowToken&&SessionState.GetString(CaptureKey(runId),"").Length!=0)
                try
                {
                    if(ReadCapture(runId).workflowToken!=workflowToken)throw new InvalidOperationException("Acquired capture journal was replaced; refusing cleanup.");
                    var cleanup=JsonUtility.FromJson<WhimTex.Tests.TestResult>(Cleanup(runId));
                    if(cleanup.status!="passed")throw new InvalidOperationException("Owned capture cleanup failed: "+cleanup.ToJson());
                }
                catch(Exception error){errors.Add(error);}
        }
        if(errors.Count==1)throw errors[0]; // Preserve ordinary SKIP/cancellation semantics when cleanup succeeded.
        if(errors.Count>1)throw new AggregateException("Capture body and owned cleanup failures retained",errors);
    }
    static string WorkflowKey(string runId)=>CaptureKey(runId)+".WorkflowAcquired";
    [Serializable] sealed class WorkflowResult
    {
        public string status,message;
        public int checks;
        public string[] failures;
        public bool recoveryRequired;
    }
    static string WorkflowEnvelope(string runId,string json)
    {
        var result=JsonUtility.FromJson<WhimTex.Tests.TestResult>(json);
        string normalized=Guid.Parse(runId).ToString("N");
        bool recovery=result.status!="running"&&SessionState.GetString(WorkflowKey(normalized),"").Length!=0&&SessionState.GetString(CaptureKey(normalized),"").Length!=0;
        if(recovery&&result.status=="passed")
            result=WhimTex.Tests.TestContext.Result("failed",result.checks,result.message,"Acquired manual capture journal remains; bridge cleanup is not manual cleanup proof.");
        return JsonUtility.ToJson(new WorkflowResult{status=result.status,checks=result.checks,message=result.message,failures=result.failures,recoveryRequired=recovery});
    }
    public static string CaptureWorkflow(string runId)
    {
        string normalized=Guid.Parse(runId).ToString("N");
        if(SessionState.GetString(CaptureKey(normalized),"").Length!=0||AppDomain.CurrentDomain.GetData("WhimTex.Tests.UnityB."+runId)!=null)
            return JsonUtility.ToJson(new WorkflowResult{status="failed",checks=0,message="Duplicate capture request did not acquire a journal or bridge.",failures=new[]{"GUID already owns a manual capture or bridge; use a new GUID."},recoveryRequired=false});
        return WorkflowEnvelope(runId,UnityBRun.Start(runId,"GradientHistoryTests.CaptureWorkflow",()=>ExecuteCaptureWorkflow(normalized)));
    }
    public static string PickerRecency() => UnityBRun.Run("GradientHistorySmoke.PickerRecency", () => ExecutePickerRecency());

    static async System.Threading.Tasks.Task ExecuteHistory() {
{

        if(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0||Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Length!=0) throw new UnityBSkipException("Close borrowed gradient/picker windows; not verified.");
        var historyDoc=UnityBRun.Create<TextureCompositor>(); historyDoc.name=Name;
        for(int i=0;i<40;i++)Call(historyDoc,"RememberColor",Color.HSVToRGB(i/40f,.7f,.8f));
        Call(historyDoc,"RememberColor",new Color(4,2,.5f,.2f));
        var type=typeof(WhimTexGradientWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexGradientSession");
        var historySession=UnityBRun.Create(type); Set(historySession,"document",historyDoc);
        var historyWindow=UnityBRun.Track(WhimTexGradientWindow.Open(historySession,"gradient"));historyWindow.name=Name;historyWindow.position=new Rect(100,100,560,420);
        Call(historyWindow,"ToggleHdr");
        
    
}
 await UnityBRun.Delay(250);

        var w=Window(); var session=Get(w,"owner"); var doc=(TextureCompositor)Get(session,"document");
        var list=(List<Color>)Get(doc,"colorHistory"); var grid=(VisualElement)Get(w,"historyGrid");
        Check(grid.childCount==list.Count&&grid.Q(className:"whimtex-picker-add-color")==null,"History without plus");
        var heading=w.rootVisualElement.Q("gradientColorHistory");
        Check(heading.worldBound.yMax<=w.rootVisualElement.Q("gradientPresets").worldBound.yMin,"History above presets");
        foreach(var space in new[]{ColorSpace.Gamma,ColorSpace.Linear})
        {
            var gradient=new WhimTexGradient{ColorSpace=space};
            gradient.SetKeys(new[]{new GradientColorKey(new Color(.2f,.3f,.4f,.6f),0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(.3f,0),new GradientAlphaKey(.9f,1)});
            Set(w,"gradient",gradient);Set(w,"selected",0);Set(w,"alphaTrack",false);Set(w,"midpointSelected",false);Call(w,"Refresh");
            if(!((UnityEditor.UIElements.ColorField)Get(w,"color")).hdr)Call(w,"ToggleHdr");
            Color expected=list[0]; if(space==ColorSpace.Linear)expected=expected.linear;expected.a=.6f;
            using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Return}))
            {e.target=grid[0];grid[0].SendEvent(e);}
            var actual=((WhimTexGradient)Get(w,"gradient")).ColorKeys[0].color;
            Check((actual-expected).maxColorComponent<.0001f&&(expected-actual).maxColorComponent<.0001f,"Exact HDR/history color and color space");
            Check(gradient.AlphaKeys[0].alpha==.3f&&gradient.ColorKeys[0].time==0,"Alpha track and key time unchanged");
            Check(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length==0,"No picker opened");
            string saved=JsonUtility.ToJson(gradient); Set(w,"alphaTrack",true);Call(w,"Refresh");
            Check(!grid.enabledSelf,"Alpha key disables history");Call(w,"SelectHistoryColor",1,list[1]);Check(JsonUtility.ToJson(gradient)==saved,"Alpha key guarded");
            Set(w,"alphaTrack",false);Set(w,"midpointSelected",true);Call(w,"Refresh");
            Check(!grid.enabledSelf,"Midpoint disables history");Call(w,"SelectHistoryColor",1,list[1]);Check(JsonUtility.ToJson(gradient)==saved,"Midpoint guarded");
        }
        Set(w,"midpointSelected",false);Call(w,"Refresh");
        Color reused=list[3];int count=list.Count;Call(doc,"RememberColor",reused);Call(w,"RefreshColorHistory");
        Check(list.Count==count&&list[0]==reused,"Matching color promoted without duplication");
        Check(grid.childCount==count,"External history change synchronized");
        Call(w,"SelectHistoryColor",2,list[2]);Check(list.Count==count,"Direct selection avoids duplicates");
        
    
    }
    public static string History(string runId) => UnityBRun.Start(runId, "GradientHistoryTests.History", ExecuteHistory);
    public static string Poll(string runId) => WorkflowEnvelope(runId,UnityBRun.Poll(runId));
    public static async System.Threading.Tasks.Task<string> Cancel(string runId) => WorkflowEnvelope(runId,await UnityBRun.Cancel(runId));
    public static string CleanupRun(string runId)
    {
        string result=WorkflowEnvelope(runId,UnityBRun.Cleanup(runId));
        string normalized=Guid.Parse(runId).ToString("N");
        if(SessionState.GetString(CaptureKey(normalized),"").Length==0)SessionState.EraseString(WorkflowKey(normalized));
        return result;
    }
}
