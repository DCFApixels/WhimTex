using System;
using System.Collections.Generic;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class DocumentLightingApiTests
{
    const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static object Call(object owner,string name,params object[] args)=>owner.GetType().GetMethod(name,F).Invoke(owner,args);
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,F).SetValue(owner,value);
    static object Get(object owner,string name)=>owner.GetType().GetField(name,F).GetValue(owner);
    static object Json(string value)=>Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json",true)
        .GetMethod("Parse",new[]{typeof(string)}).Invoke(null,new object[]{value});
    public static string Run()=>TestContext.Run("Lighting agent contract and detached native panel",t=>
    {
        var doc=ScriptableObject.CreateInstance<WhimTexDocument>();
        WhimTexWindow window=null;
        WhimTexDocument windowDocument=null;
        try
        {
            void Apply(string json)=>typeof(WhimTexApi).GetMethod("ApplyOperation",F).Invoke(null,new object[]{doc,Json(json),new Dictionary<string,Layer>(),true,null,null});
            string Revision()=>(string)typeof(WhimTexApi).GetMethod("Revision",F).Invoke(null,new object[]{doc});
            string initial=Revision();
            t.Equal(Color.white, doc.ambientColor, "Ambient defaults to neutral white");
            Apply("{op:'lighting',settings:{ambientColor:[2,0.4,0.1,0.5]}}");
            t.Equal(new Color(2,.4f,.1f,1),doc.ambientColor,"API sets HDR ambient; alpha is unused");
            t.True(initial!=Revision(),"Ambient edit changes revision");
            Apply("{op:'light',action:'add',id:'key',settings:{type:'Directional',color:[4,2,1,1],intensity:3}}");
            Apply("{op:'light',action:'add',id:'point',settings:{type:'Point',position:[100,200,300],range:900,falloff:3}}");
            t.Equal(2,doc.lights.Count,"Two API lights");t.True(initial!=Revision(),"Light edits change document revision");
            var snapshot=ApiJson.Parse(typeof(WhimTexApi).GetMethod("Snapshot",F).Invoke(null,new object[]{doc,null}).ToString());
            t.Equal("point",(string)snapshot["lights"][1]["id"],"Inspect stable light ID");
            t.Equal(1,(int)snapshot["lights"][1]["index"],"Inspect shader index");
            t.Equal("2", snapshot["ambientColor"][0].ToString(), "Inspect ambient color");
            Apply("{op:'light',action:'set',id:'point',settings:{enabled:false,intensity:7}}");
            t.True(!doc.lights[1].enabled,"Disable source through API");t.Equal(7f,doc.lights[1].intensity,"Edit intensity");
            var addResult=ApiJson.Parse(typeof(WhimTexApi).GetMethod("OperationResult",F).Invoke(null,new object[]{doc,Json("{op:'light',action:'add'}"),0,null}).ToString());
            t.Equal("point",(string)addResult["lightId"],"Batch add returns source ID");
            foreach(string invalid in new[]{"{op:'light',action:'add',id:'point'}","{op:'light',id:'missing'}",
                "{op:'light',id:'point',settings:{direction:[0,0,0]}}","{op:'light',id:'point',settings:{range:-1}}",
                "{op:'light',id:'point',settings:{position:[1,2]}}","{op:'light',id:'point',settings:{type:'Unknown'}}",
                "{op:'light',id:'point',settings:{angle:180}}","{op:'light',id:'point',settings:{softness:-0.1}}"})
            {
                string before=Revision();bool rejected=false;
                try{Apply(invalid);}catch(TargetInvocationException e){rejected=e.InnerException is Exception;}
                t.True(rejected,"Invalid light operation rejected");t.Equal(before,Revision(),"Rejected operation does not mutate light");
            }
            Apply("{op:'light',action:'add',id:'spot',settings:{type:'Spot',angle:42,softness:0.6}}");
            t.Equal(Vector3.back,doc.lights[2].direction,"Spot defaults toward canvas");
            snapshot=ApiJson.Parse(typeof(WhimTexApi).GetMethod("Snapshot",F).Invoke(null,new object[]{doc,null}).ToString());
            t.Equal("42",snapshot["lights"][2]["angle"].ToString(),"Inspect Spot angle");
            t.Near(.6,double.Parse(snapshot["lights"][2]["softness"].ToString(),System.Globalization.CultureInfo.InvariantCulture),.00001,"Inspect Spot softness");
            Apply("{op:'light',action:'remove',id:'spot'}");
            Apply("{op:'light',action:'add',id:'area',settings:{type:'Area',areaShape:'Disk',size:[320,180],rotation:35}}");
            var area=doc.lights.Find(l=>l.id=="area");
            t.Equal(Vector3.back,area.direction,"Area defaults toward canvas");
            t.Equal(WhimTexAreaLightShape.Disk,area.areaShape,"Area shape from API");
            t.Equal(new Vector2(320,180),area.size,"Area dimensions from API");
            snapshot=ApiJson.Parse(typeof(WhimTexApi).GetMethod("Snapshot",F).Invoke(null,new object[]{doc,null}).ToString());
            t.Equal("Disk",(string)snapshot["lights"][2]["areaShape"],"Snapshot includes area shape");
            foreach(string fields in new[]{"size:[0,10]","size:[10]","areaShape:'Triangle'","cookie:'bad'"})
            {
                bool rejected=false; string before=Revision();
                try{Apply("{op:'light',id:'area',settings:{"+fields+"}}");}catch(TargetInvocationException){rejected=true;}
                t.True(rejected,"Invalid Area/Cookie settings rejected");t.Equal(before,Revision(),"Invalid Area edit is atomic");
            }
            Apply("{op:'light',id:'point',settings:{type:'Spot',cookie:{$asset:{guid:'ffffffffffffffffffffffffffffffff',path:'Assets/missing-cookie.png',localId:'2800000',type:'UnityEngine.Texture2D'}}}}");
            snapshot=ApiJson.Parse(typeof(WhimTexApi).GetMethod("Snapshot",F).Invoke(null,new object[]{doc,null}).ToString());
            t.Equal("Assets/missing-cookie.png",(string)snapshot["lights"][1]["cookie"]["$asset"]["path"],"Missing Cookie identity retained for agent");
            string missingJson=WhimTexDocumentJson.Write(doc).Json;
            using(var loaded=WhimTexDocumentJson.Read(missingJson))
            {
                loaded.Document.RemoveLight("key");
                string shifted=WhimTexDocumentJson.Write(loaded.Document).Json;
                t.True(shifted.Contains("missing-cookie.png"),"Removing an earlier light retains missing Cookie on its stable owner");
                var restored=loaded.Document.lights.Find(item=>item.id=="point");
                Undo.IncrementCurrentGroup();Undo.RegisterCompleteObjectUndo(loaded.Document,"Clear missing Cookie");
                typeof(WhimTexDocumentJson).GetMethod("SetLightCookie",F).Invoke(null,new object[]{restored,null,null});
                typeof(WhimTexDocumentJson).GetMethod("CaptureMissingAssets",F).Invoke(null,new object[]{loaded.Document});
                Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();
                t.True(!WhimTexDocumentJson.Write(loaded.Document).Json.Contains("missing-cookie.png"),"Native field clear removes persisted missing identity");
                Undo.PerformUndo();t.True(WhimTexDocumentJson.Write(loaded.Document).Json.Contains("missing-cookie.png"),"Undo restores missing Cookie identity");
                Undo.PerformRedo();t.True(!WhimTexDocumentJson.Write(loaded.Document).Json.Contains("missing-cookie.png"),"Redo keeps missing Cookie cleared");
                Undo.ClearUndo(loaded.Document);
            }
            Apply("{op:'light',id:'point',settings:{type:'Point',cookie:null}}");
            t.True(!WhimTexDocumentJson.Write(doc).Json.Contains("missing-cookie.png"),"API clear removes persisted Cookie identity");
            Apply("{op:'light',action:'remove',id:'area'}");
            Apply("{op:'light',action:'remove',id:'key'}");t.Equal("point",doc.lights[0].id,"Remove retains other source");
            while(doc.lights.Count<WhimTexDocument.MaximumLights)Apply("{op:'light',action:'add'}");
            bool limited=false;try{Apply("{op:'light',action:'add'}");}catch(TargetInvocationException){limited=true;}
            t.True(limited,"Light count budget enforced");

            // No Show/Focus/native input: inspect the same field tree built by the real drawer.
            window=ScriptableObject.CreateInstance<WhimTexWindow>();
            windowDocument=(WhimTexDocument)Get(window,"activeDocument");
            var source=windowDocument.AddLight(WhimTexLightType.Point);
            Set(window,"lightsExpanded",true); Set(window,"selectedLightId",source.id);
            var root=window.rootVisualElement; Call(window,"BuildLightsDrawer",root);Call(window,"RefreshLightsPanel");
            t.Equal(2,root.Query<Vector3Field>().ToList().Count,"Native position/direction fields");
            var numbers=root.Query<FloatField>().ToList();
            t.True(numbers.Exists(f=>f.label=="Intensity") && numbers.Exists(f=>f.label=="Range (px)") && numbers.Exists(f=>f.label=="Falloff"),"Native intensity/range/falloff fields");
            t.Equal(1,root.Query<EnumField>().ToList().FindAll(field=>field.label=="Type").Count,"Native type selector");
            t.Equal(1,root.Query<EnumField>().ToList().FindAll(field=>field.label=="Shape").Count,"Native area shape selector");
            t.True(root.Query<DCFApixels.WhimTex.WhimTexColorField>().ToList().Exists(field=>field.label=="Ambient Color" && field.hdr && !field.showAlpha),"Lights exposes native HDR ambient color field");
            var position=root.Query<Vector3Field>().ToList()[0];
            // An unshown EditorWindow has no event-dispatching panel. Exercise the
            // shared editing callback and verify native fields refresh from the model.
            Call(window,"EditLight",(Action)(()=>source.position=new Vector3(80,90,150)),"Edit Light");
            t.Equal(source.position,position.value,"Native field follows edited model");
            // Build the manipulator without showing a window; no native pointer input.
            var viewport=Get(window,"canvasViewport");
            var canvasType=typeof(WhimTexWindow).GetNestedType("CanvasElement",F);
            var canvas=(VisualElement)Activator.CreateInstance(canvasType,F,null,new[]{viewport},null);
            Set(window,"toolkitCanvas",canvas);Call(window,"BuildLightsCanvasTool");
            var manip=Get(window,"lightManipulator");
            Set(manip,"pointer",0);
            t.True((bool)typeof(WhimTexWindow).GetMethod("IsDocumentBusyForApi",F).Invoke(null,new object[]{windowDocument}),"Light drag blocks agent edits");
            Set(manip,"pointer",-1);
            t.True(!(bool)typeof(WhimTexWindow).GetMethod("IsDocumentBusyForApi",F).Invoke(null,new object[]{windowDocument}),"Finished drag releases API guard");
        }
        finally
        {
            if(window!=null)Object.DestroyImmediate(window);
            if(windowDocument!=null){Undo.ClearUndo(windowDocument);Object.DestroyImmediate(windowDocument);}
            Object.DestroyImmediate(doc);
        }
    });
}
