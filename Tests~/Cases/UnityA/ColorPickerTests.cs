using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class ColorPickerTests
{
static WhimTex.Tests.TestContext T;
static WhimTex.Tests.UnityA.UnityAScope Scope;
static System.Threading.CancellationToken Cancellation;

    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static int checks;
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name,F).Invoke(target,args);
    static object Get(object target, string name) => target.GetType().GetField(name,F).GetValue(target);
    static void Check(bool ok, string label) { T.True(ok, label); }
    static void Near(Color a, Color b, string label) => Check(Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a)<.0002f,label);
    static WhimTexColorPicker Open(Color initial, bool hdr, bool alpha, WhimTexColorRange range, TextureCompositor doc, Action<Color> changed, Action<bool> mode = null, Func<bool> valid = null)
        => Scope.OwnWindow((WhimTexColorPicker)typeof(WhimTexColorPicker).GetMethod("Open",F).Invoke(null,new object[]{initial,hdr,alpha,range,doc,changed,mode,valid}));
    static List<Color> History(TextureCompositor doc) => (List<Color>)Get(doc,"colorHistory");
    private static string BodyRun()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length != 0), "Close the borrowed active picker before this case");
        checks = 0; var focus=EditorWindow.focusedWindow;
        var doc=Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>());
        var other=Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>());
        WhimTexColorPicker picker=null; TextureCompositor copy=null;
        try
        {
            Color initial=new Color(4,2,1,.35f), observed=initial; bool mode=false; int edits=0;
            picker=Open(initial,false,true,WhimTexColorRange.Switchable,doc,c=>{observed=c;edits++;},h=>mode=h);
            Check(!picker.rootVisualElement.Query<Button>().ToList().Any(b=>b.text=="OK"||b.text=="Cancel"),"No confirmation buttons");
            Check(edits==0,"Opening does not change source");
            var hdr=(Toggle)Get(picker,"hdrControl"); Check(hdr.enabledSelf,"Switchable HDR");
            hdr.value=true; Near(observed,initial,"HDR toggle preserves source"); Check(mode,"Mode callback");
            hdr.value=false; Near(observed,initial,"Standard toggle preserves HDR source");
            Call(picker,"SetColor",new Color(.1f,.2f,.3f,.7f),true);
            Check(History(doc).Count==0,"Intermediate edits not remembered");
            using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape}))
            { e.target=picker.rootVisualElement; picker.rootVisualElement.SendEvent(e); }
            Check(picker==null,"Escape closes picker");
            picker=null; Near(observed,initial,"Escape restores exact HDR color");
            Check(History(doc).Count==0&&!mode,"Cancel restores mode and leaves history empty");
            foreach(var range in new[]{WhimTexColorRange.StandardOnly,WhimTexColorRange.HdrOnly})
            {
                picker=Open(Color.white,range!=WhimTexColorRange.HdrOnly,false,range,doc,c=>observed=c);
                Check(!((Toggle)Get(picker,"hdrControl")).enabledSelf,"Fixed range toggle disabled");
                Call(picker,"SetColor",new Color(8,2,.3f,.1f),true);
                Near(observed,range==WhimTexColorRange.HdrOnly?new Color(8,2,.3f,1):new Color(1,1,.3f,1),"Range and hidden alpha");
                Call(picker,"Finish",true); picker=null;
            }
            Check(History(doc).Count==2&&History(other).Count==0,"History isolated per document");
            Color first=History(doc)[0],second=History(doc)[1];
            Call(doc,"RememberColor",first); Check(History(doc).Count==2,"Duplicate suppression");
            Call(doc,"MoveHistoryColor",0,1); Near(History(doc)[1],first,"History reordered");
            Call(doc,"RemoveHistoryColor",0); Near(History(doc)[0],first,"History deletion");
            Call(doc,"RememberColor",new Color(float.NaN,0,0)); Check(History(doc).Count==1,"Nonfinite history rejected");
            var assembly=typeof(TextureCompositor).Assembly;
            var containerType=assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentContainer");
            using(var container=(IDisposable)Activator.CreateInstance(containerType,true))
            {
                var serializer=assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer");
                byte[] bytes=(byte[])serializer.GetMethod("Serialize",F).Invoke(null,new object[]{doc,container});
                var readResult = serializer.GetMethod("Deserialize", F).Invoke(null, new object[]{bytes,container,typeof(TextureCompositor),null,false});
                copy=(TextureCompositor)readResult.GetType().GetProperty("Model", F).GetValue(readResult);
                Check(History(copy).Count==1,"Document serializer retains history"); Near(History(copy)[0],first,"History preserves RGBA");
            }
            bool valid=true; observed=Color.red;
            picker=Open(Color.red,true,true,WhimTexColorRange.Switchable,other,c=>observed=c,null,()=>valid);
            Call(picker,"SetColor",Color.green,true); valid=false;
            Call(picker,"SetColor",Color.blue,true); Near(observed,Color.green,"Stale picker cannot mutate source");
            Call(picker,"Finish",true); picker=null; Check(History(other).Count==0,"Stale picker cannot append history");
            picker=Open(Color.red,true,true,WhimTexColorRange.Switchable,other,c=>observed=c);
            ((TextField)Get(picker,"hex")).value="00517D";
            Near(observed,new Color(0,81/255f,125/255f,1),"HEX input");
            var before=observed; ((TextField)Get(picker,"hex")).value="not a color"; Near(observed,before,"Invalid HEX does not change source");
            ((Slider)Get(picker,"intensity")).value=2;
            Near(observed,new Color(0,324/255f,500/255f,1),"Exposure scales RGB");
            Call(picker,"Finish",true); picker=null;
            Check(History(other).Count==1,"One accepted session adds one color");
            picker=Open(Color.red,true,true,WhimTexColorRange.Switchable,other,c=>observed=c);
            Call(picker,"SetColor",Color.green,true);
            picker.Close(); picker=null;
            Near(observed,Color.green,"Closing window confirms current color");
            Check(History(other).Count==2&&History(other)[0]==Color.green,"Closing window prepends confirmed color");
            Color manual=new Color(3,.2f,.4f,.5f);
            picker=Open(manual,true,true,WhimTexColorRange.Switchable,other,c=>observed=c);
            void AddColor()
            {
                var button=picker.rootVisualElement.Q(className:"whimtex-picker-add-color");
                using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}
            }
            AddColor();
            Check(picker!=null,"Add color keeps picker open");
            Check(History(other).Count==3,"Plus button adds current color");
            Near(History(other)[0],manual,"Plus button prepends exact HDR and alpha");
            AddColor(); Check(History(other).Count==3,"Plus button suppresses duplicates");
            Call(picker,"Finish",false); picker=null;
            Check(History(other).Count==3,"Explicit history addition survives canceled color selection");
            return null;
        }
        finally
        {
            if(picker!=null) Call(picker,"Finish",false);
            foreach(var d in new[]{doc,other,copy}) if(d!=null){Undo.ClearUndo(d);UnityEngine.Object.DestroyImmediate(d);}
            if(focus!=null)focus.Focus();
        }
    }

    private static string BodyBrushSampling()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0), "Close the borrowed active picker before this case");
        checks=0;
        var focus=EditorWindow.focusedWindow;
        var doc=Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>());
        var host=Scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexGradientWindow>());
        WhimTexColorPicker picker=null;
        try
        {
            host.name=Scope.Tag + "-brush-picker-sync";host.ShowUtility();host.rootVisualElement.Clear();
            var field=new WhimTexColorField{hdr=true,showAlpha=true};host.rootVisualElement.Add(field);
            var owner=new object();var otherOwner=new object();
            typeof(WhimTexColorField).GetField("PickerContext",F).SetValue(field,owner);
            typeof(WhimTexColorField).GetField("Document",F).SetValue(field,(Func<TextureCompositor>)(()=>doc));
            Color original=new Color(2,.5f,.2f,.37f),observed=original;
            field.SetValueWithoutNotify(original);int edits=0;
            field.RegisterValueChangedCallback(e=>{observed=e.newValue;edits++;});
            Call(field,"OpenPicker");picker=Scope.OwnWindow(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single());
            bool Sample(object context,Color color)=>(bool)typeof(WhimTexColorPicker).GetMethod("TryApplySample",F).Invoke(null,new[]{context,(object)color});
            Color sample=new Color(.2f,.4f,.6f,original.a);
            Check(!Sample(otherOwner,sample),"Other window cannot synchronize picker");
            Near(observed,original,"Other window leaves brush unchanged");
            Check(Sample(owner,sample),"Brush picker accepts its owner's sample");
            Near(observed,sample,"Sample updates brush through field callback");
            Near((Color)Get(picker,"color"),sample,"Picker color follows sample");
            Check(((TextField)Get(picker,"hex")).value=="336699","HEX follows sample");
            Color.RGBToHSV(sample,out float h,out float s,out float v);
            Check(Mathf.Abs((float)Get(picker,"hue")-h)<.0001f&&Mathf.Abs((float)Get(picker,"saturation")-s)<.0001f&&Mathf.Abs((float)Get(picker,"brightness")-v)<.0001f,"Hue ring and SV marker follow sample");
            ((PopupField<string>)Get(picker,"mode")).index=0;
            Near(new Color(((Slider[])Get(picker,"channels"))[0].value/255f,((Slider[])Get(picker,"channels"))[1].value/255f,((Slider[])Get(picker,"channels"))[2].value/255f,sample.a),sample,"Channel values follow sample");
            Check(edits==1&&History(doc).Count==0,"One callback without intermediate history");
            Check(Sample(owner,sample)&&edits==1,"Repeated sample causes no feedback loop");
            using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape})){e.target=picker.rootVisualElement;picker.rootVisualElement.SendEvent(e);}
            picker=null;Near(observed,original,"Escape restores opening brush color including HDR and alpha");
            Check(!Sample(owner,sample),"Closed picker does not intercept sampling");
            Call(field,"OpenPicker");picker=Scope.OwnWindow(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single());
            Sample(owner,sample);picker.Close();picker=null;
            Near(History(doc)[0],sample,"Closing remembers sampled color");
            picker=Open(Color.red,true,true,WhimTexColorRange.Switchable,doc,c=>observed=c);
            Check(!Sample(owner,sample),"Unrelated picker in same document is untouched");
            Near((Color)Get(picker,"color"),Color.red,"Layer or gradient picker retains its color");
            Call(picker,"Finish",false);picker=null;
            Call(field,"OpenPicker");picker=Scope.OwnWindow(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single());
            field.SetEnabled(false);Check(!Sample(owner,Color.green),"Stale or disabled field is not synchronized");
            return null;
        }
        finally
        {
            if(picker!=null)Call(picker,"Finish",false);
            host.Close();Undo.ClearUndo(doc);UnityEngine.Object.DestroyImmediate(doc);
            if(focus!=null)focus.Focus();
        }
    }
    private static string LayoutSetup()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0), "Close the borrowed active picker before this case");
        var doc=Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>()); doc.name=(Scope.Tag + "-picker-layout"); doc.hideFlags=HideFlags.HideAndDontSave;
        for(int i=0;i<20;i++)Call(doc,"RememberColor",Color.HSVToRGB(i/20f,.75f,.8f));
        var window=Open(new Color(.1f,.5f,.8f,1),true,true,WhimTexColorRange.Switchable,doc,_=>{});
        window.name=(Scope.Tag + "-picker-layout");
        return null;
    }

    private static string BodyFields()
    {
        T.True(!(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Length!=0), "Close the borrowed active picker before this case");
        checks=0;
        var focus=EditorWindow.focusedWindow;
        var host=Scope.OwnWindow(ScriptableObject.CreateInstance<WhimTexGradientWindow>());
        var first=Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>());
        var second=Scope.OwnObject(ScriptableObject.CreateInstance<TextureCompositor>());
        WhimTexColorPicker picker=null;
        try
        {
            host.name=Scope.Tag + "-color-field-integration";host.ShowUtility();host.rootVisualElement.Clear();
            var field=new WhimTexColorField("Color"){hdr=false,showAlpha=true};
            host.rootVisualElement.Add(field);
            TextureCompositor active=first;
            typeof(WhimTexColorPicker).GetMethod("SetDocument",F).Invoke(null,new object[]{host.rootVisualElement,(Func<TextureCompositor>)(()=>active)});
            Color source=new Color(4,2,1,.5f),initial=source;
            typeof(WhimTexColorField).GetField("ReadPickerColor",F).SetValue(field,(Func<Color>)(()=>source));
            field.SetValueWithoutNotify(new Color(1,.5f,.25f,.5f));
            field.RegisterValueChangedCallback(e=>source=e.newValue);
            var mouse=new Event{type=EventType.MouseDown,button=0};
            using(var e=PointerDownEvent.GetPooled(mouse)){e.target=field;field.SendEvent(e);}
            picker=Scope.OwnWindow(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single());
            Near((Color)Get(picker,"color"),initial,"Field opens with raw HDR, not bounded display");
            Check((TextureCompositor)Get(picker,"document")==first,"Field picks ancestor document");
            Call(picker,"SetColor",new Color(1,.5f,.25f,.5f),true);
            Near(source,new Color(1,.5f,.25f,.5f),"Same display color replaces HDR source");
            Call(picker,"Finish",false);picker=null;
            Near(source,initial,"Field cancel restores raw source");
            Call(field,"OpenPicker");picker=Scope.OwnWindow(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single());
            active=second;
            Call(picker,"SetColor",Color.green,true);
            Near(source,initial,"Switching document invalidates old field picker");
            Call(picker,"Finish",true);picker=null;
            Check(History(first).Count==0&&History(second).Count==0,"Stale field cannot write either history");
            Call(field,"OpenPicker");picker=Scope.OwnWindow(Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single());
            Call(picker,"SetColor",Color.blue,true);Call(picker,"Finish",true);picker=null;
            Check(History(second).Count==1,"Valid field records in new document");
            Near(source,Color.blue,"Valid field applies source edit");
            return null;
        }
        finally
        {
            if(picker!=null)Call(picker,"Finish",false);
            host.Close();
            foreach(var doc in new[]{first,second}){Undo.ClearUndo(doc);UnityEngine.Object.DestroyImmediate(doc);}
            if(focus!=null)focus.Focus();
        }
    }
    private static string LayoutVerify()
    {
        var picker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var root=picker.rootVisualElement;
        var plane=root.Q(className:"whimtex-picker-plane");var ring=root.Q(className:"whimtex-picker-ring");
        Check(plane.worldBound.width>=100&&Mathf.Abs(plane.worldBound.width-plane.worldBound.height)<1,"Square color plane laid out");
        Check(Vector2.Distance(plane.worldBound.center,ring.worldBound.center)<1,"Square centered within hue ring");
        Check(root.Query(className:"whimtex-picker-ramp").ToList().Count==5,"Gradient tracks for RGB/HSV, alpha and exposure");
        Check(root.Query<Foldout>().ToList().Single().text=="History","History uses foldout without Swatches");
        foreach(var button in root.Query<Button>().ToList()) Check(root.worldBound.Contains(button.worldBound.center),"Action inside window: "+button.text);
        var history=(VisualElement)Get(picker,"history");
        Check(history.childCount==21,"History swatches and add button visible");
        Check(history[0].ClassListContains("whimtex-picker-add-color")&&(bool)Get(history[0],"showPlus")&&history[0].childCount==0,"Plus is drawn inside the first swatch without a text label");
        var from=history[1]; var to=history[4];
        var cursor=from.style.cursor.value.texture;
        Check(cursor!=null&&cursor.isReadable&&cursor.format==TextureFormat.RGBA32&&cursor.mipmapCount==1,"Eyedropper texture meets cursor requirements");
        Check(history[0].style.cursor.value.texture==null,"Plus keeps the default cursor");
        Check(history[0].GetType()==from.GetType()&&!history[0].ClassListContains(Button.ussClassName),"Plus uses the same color surface, not a native Button");
        Check(Mathf.Abs(history[0].worldBound.width-from.worldBound.width)<.1f&&Mathf.Abs(history[0].worldBound.height-from.worldBound.height)<.1f,"Plus button matches swatch size");
        void Pointer(VisualElement target, EventType type, Vector2 point)
        {
            var e=new Event{type=type,button=0,mousePosition=point};
            if(type==EventType.MouseDown){using(var p=PointerDownEvent.GetPooled(e)){p.target=target;target.SendEvent(p);}}
            else if(type==EventType.MouseDrag){using(var p=PointerMoveEvent.GetPooled(e)){p.target=target;target.SendEvent(p);}}
            else {using(var p=PointerUpEvent.GetPooled(e)){p.target=target;target.SendEvent(p);}}
        }
        var doc=(TextureCompositor)Get(picker,"document");Color first=History(doc)[0];
        Pointer(from,EventType.MouseDown,from.worldBound.center);
        Pointer(from,EventType.MouseDrag,history[0].worldBound.center);
        Check(!history[0].ClassListContains("whimtex-picker-chip--target"),"Plus button is not a reorder target");
        Pointer(from,EventType.MouseDrag,to.worldBound.center);Pointer(from,EventType.MouseUp,to.worldBound.center);
        Near(History(doc)[3],first,"History drag reorders");
        return null;
    }
    private static string HistorySelectionVerify()
    {
        var picker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var history=(VisualElement)Get(picker,"history");
        var doc=(TextureCompositor)Get(picker,"document");
        var before=History(doc).ToArray();
        var chip=history[5];
        var mouse=new Event{type=EventType.MouseDown,button=0,mousePosition=chip.worldBound.center};
        using(var e=PointerDownEvent.GetPooled(mouse)){e.target=chip;chip.SendEvent(e);}
        mouse.type=EventType.MouseUp;
        using(var e=PointerUpEvent.GetPooled(mouse)){e.target=chip;chip.SendEvent(e);}
        Near((Color)Get(picker,"color"),before[4],"History click applies selected color");
        Check(History(doc).SequenceEqual(new[]{before[4]}.Concat(before.Take(4)).Concat(before.Skip(5))),"Selected color moves first, preserving other colors and count");
        Check(history[0].ClassListContains("whimtex-picker-add-color")&&(int)history[1].userData==0,"Selected color occupies first slot after plus");
        Call(picker,"SelectHistoryColor",0,before[4]);
        Check(History(doc).Count==before.Length&&History(doc)[0]==before[4],"Selecting first color does not duplicate it");
        return null;
    }
    private static string MinimumSize()
    {
        var picker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        picker.position=new Rect(picker.position.position,picker.minSize);
        return null;
    }
    private static string AddSwatchVerify()
    {
        var picker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        Call(picker,"SetColor",Color.gray,true);
        var add=picker.rootVisualElement.Q(className:"whimtex-picker-add-color");
        var mouse=new Event{type=EventType.MouseDown,button=0,mousePosition=add.worldBound.center};
        using(var e=PointerDownEvent.GetPooled(mouse)){e.target=add;add.SendEvent(e);}
        mouse.type=EventType.MouseUp;
        using(var e=PointerUpEvent.GetPooled(mouse)){e.target=add;add.SendEvent(e);}
        var doc=(TextureCompositor)Get(picker,"document");
        Near(History(doc)[0],Color.gray,"Clicking gray plus swatch prepends current color");
        return null;
    }
    private static string WheelVerify()
    {
        var picker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var ring=picker.rootVisualElement.Q(className:"whimtex-picker-ring");
        var plane=picker.rootVisualElement.Q(className:"whimtex-picker-plane");
        void Click(VisualElement target,Vector2 point)
        {
            var source=new Event{type=EventType.MouseDown,button=0,mousePosition=target.LocalToWorld(point)};
            using(var e=PointerDownEvent.GetPooled(source)){e.target=target;target.SendEvent(e);}
            source.type=EventType.MouseUp;
            using(var e=PointerUpEvent.GetPooled(source)){e.target=target;target.SendEvent(e);}
        }
        Click(ring,new Vector2(ring.contentRect.width*.5f,ring.contentRect.height*.05f));
        Check(Mathf.Abs((float)Get(picker,"hue")-.25f)<.001f,"Hue ring maps top to yellow-green");
        Click(ring,ring.contentRect.center);
        Check(Mathf.Abs((float)Get(picker,"hue")-.25f)<.001f,"Empty wheel center does not change hue");
        Click(plane,new Vector2(plane.contentRect.width*.75f,plane.contentRect.height*.25f));
        Check(Mathf.Abs((float)Get(picker,"saturation")-.75f)<.001f&&Mathf.Abs((float)Get(picker,"brightness")-.75f)<.001f,"Central square changes S and V independently");
        foreach(var ramp in picker.rootVisualElement.Query(className:"whimtex-picker-ramp").ToList())
            Check(ramp.worldBound.width>50&&ramp.worldBound.height>=12,"Gradient track has visible dimensions");
        var before=(VisualElement)Get(picker,"before");var after=(VisualElement)Get(picker,"after");
        Check(before.worldBound.yMax<ring.worldBound.yMin&&after.worldBound.xMin>=before.worldBound.xMax-1,"Original/new samples at upper right");
        return null;
    }
    private static string DragRemovalVerify()
    {
        var picker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var history=(VisualElement)Get(picker,"history");
        var scroll=(ScrollView)Get(picker,"historyScroll");
        var doc=(TextureCompositor)Get(picker,"document");
        var chip=history[1];var inside=chip.worldBound.center;var area=scroll.worldBound;
        var before=History(doc).ToArray();Color selected=(Color)Get(picker,"color");
        void Pointer(EventType type,Vector2 point)
        {
            var source=new Event{type=type,button=0,mousePosition=point};
            if(type==EventType.MouseDown){using(var e=PointerDownEvent.GetPooled(source)){e.target=chip;chip.SendEvent(e);}}
            else if(type==EventType.MouseDrag){using(var e=PointerMoveEvent.GetPooled(source)){e.target=chip;chip.SendEvent(e);}}
            else {using(var e=PointerUpEvent.GetPooled(source)){e.target=chip;chip.SendEvent(e);}}
        }
        bool Red()=>chip.ClassListContains("whimtex-picker-chip--remove");
        var outside=new Vector2(area.center.x,area.yMin-24);
        Pointer(EventType.MouseDown,inside);
        foreach(var point in new[]{outside,new Vector2(area.center.x,area.yMax+24),new Vector2(area.xMin-24,area.center.y),new Vector2(area.xMax+24,area.center.y)})
        {
            Pointer(EventType.MouseDrag,point);Check(Red(),"Outside history marks pending deletion");
            Check(History(doc).SequenceEqual(before),"Dragging alone does not delete");
            Pointer(EventType.MouseDrag,inside);Check(!Red(),"Returning clears pending deletion");
        }
        Pointer(EventType.MouseDrag,outside);
        using(var e=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape})){e.target=chip;chip.SendEvent(e);}
        Check(picker!=null&&!Red()&&!chip.HasPointerCapture(PointerId.mousePointerId),"Escape cancels drag without closing picker");
        Pointer(EventType.MouseUp,outside);Check(History(doc).SequenceEqual(before),"Canceled release cannot delete");
        Pointer(EventType.MouseDown,inside);Pointer(EventType.MouseDrag,outside);
        chip.ReleasePointer(PointerId.mousePointerId);
        using(var e=PointerCaptureOutEvent.GetPooled(chip,null,PointerId.mousePointerId)){chip.SendEvent(e);}
        Check(!Red(),"Capture loss clears pending deletion");
        Pointer(EventType.MouseUp,outside);Check(History(doc).SequenceEqual(before),"Capture loss cannot delete");
        Pointer(EventType.MouseDown,inside);Pointer(EventType.MouseDrag,outside);Pointer(EventType.MouseUp,outside);
        Check(History(doc).Count==before.Length-1&&History(doc).SequenceEqual(before.Skip(1)),"Outside release removes only dragged color");
        Near((Color)Get(picker,"color"),selected,"History deletion does not edit current color");
        return null;
    }
    private static string MinimumVerify()
    {
        var picker=Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Single(x=>x.name==(Scope.Tag + "-picker-layout"));
        var root=picker.rootVisualElement;
        foreach(var item in root.Query<Button>().ToList().Cast<VisualElement>().Concat(root.Query<Slider>().ToList()))
            Check(item.worldBound.yMin>=root.worldBound.yMin&&item.worldBound.yMax<=root.worldBound.yMax,"Control fits minimum height: "+item.GetType().Name);
        return null;
    }
    private static string LayoutCleanup()
    {
        foreach(var picker in Resources.FindObjectsOfTypeAll<WhimTexColorPicker>().Where(x=>x.name==(Scope.Tag + "-picker-layout")))Call(picker,"Finish",false);
        foreach(var doc in Resources.FindObjectsOfTypeAll<TextureCompositor>().Where(x=>x.name==(Scope.Tag + "-picker-layout"))){Undo.ClearUndo(doc);UnityEngine.Object.DestroyImmediate(doc);}
        return null;
    }

private static async Task<string> BodyLayoutScenario() {
LayoutSetup(); try { await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); LayoutVerify(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(200, Cancellation); HistorySelectionVerify(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(200, Cancellation); AddSwatchVerify(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(200, Cancellation); WheelVerify(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(200, Cancellation); DragRemovalVerify(); MinimumSize(); await WhimTex.Tests.UnityA.UnityAAsync.Delay(300, Cancellation); MinimumVerify(); } finally { LayoutCleanup(); }
return null;
}

public static string InspectApi()
{
    var type = typeof(WhimTexColorPicker);
    return string.Join("\n", Array.ConvertAll(Array.FindAll(type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
        m => m.Name == "Open"), m => m + " : " + string.Join(",", Array.ConvertAll(m.GetParameters(), p => p.Name))));
}
public static string Run() => WhimTex.Tests.TestContext.Run("Run", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyRun(); } finally { T = null; Scope = null; } }));
public static string BrushSampling() => WhimTex.Tests.TestContext.Run("BrushSampling", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyBrushSampling(); } finally { T = null; Scope = null; } }));
public static string Fields() => WhimTex.Tests.TestContext.Run("Fields", context => WhimTex.Tests.UnityA.UnityAScope.RunOwned(scope => { T = context; Scope = scope; try { BodyFields(); } finally { T = null; Scope = null; } }));
public static string Start(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Start(runId, (context, cancellation) => WhimTex.Tests.UnityA.UnityAScope.RunOwnedAsync(async scope => { T = context; Scope = scope; Cancellation = cancellation; try { await BodyLayoutScenario(); } finally { T = null; Scope = null; } }));
public static string Poll(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Poll(runId);
public static System.Threading.Tasks.Task<string> Cancel(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cancel(runId);
public static System.Threading.Tasks.Task<string> Cleanup(string runId) => WhimTex.Tests.UnityA.UnityAAsync.Cleanup(runId);
}
