using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class MultiLayerTransformSmoke
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    public static string Main()
    {
        var doc=ScriptableObject.CreateInstance<TextureCompositor>(); doc.width=doc.height=128;
        int checks=0;
        void Check(bool ok,string label) { if(!ok)throw new Exception(label); checks++; }
        var type=typeof(TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.MultiLayerTransform");
        object New(params Layer[] layers) => Activator.CreateInstance(type,F,null,new object[]{doc,Ids(layers)},null);
        List<string> Ids(params Layer[] layers) { var ids=new List<string>(); foreach(var l in layers) { if(string.IsNullOrEmpty(l.Id)) typeof(Layer).GetMethod("EnsureId",F).Invoke(l,new object[]{new HashSet<string>()}); ids.Add(l.Id); } return ids; }
        object Call(object state,string name,params object[] args)=>type.GetMethod(name,F).Invoke(state,args);
        TextureTransform Frame(object state)=>(TextureTransform)type.GetProperty("Frame",F).GetValue(state);
        ProjectiveMatrix World(Layer l)=>((TextureTransform)typeof(TextureCompositor).GetMethod("GetCanvasTransform",F).Invoke(doc,new object[]{l})).ToMatrix(128,128);
        void Same(ProjectiveMatrix a,ProjectiveMatrix b,string label)
        {
            foreach(var p in new[]{new Double2(0,0),new Double2(1,0),new Double2(1,1),new Double2(.3,.7)})
            { var x=a.Point(p);var y=b.Point(p);Check(Math.Abs(x.x-y.x)+Math.Abs(x.y-y.y)<1e-10,label); }
        }
        try
        {
            Layer group=new GroupLayerBehaviour(), farChild=new ShapeLayerBehaviour(), other=new ShapeLayerBehaviour();
            group.children.Add(farChild); doc.layers.Add(group);doc.layers.Add(other);
            farChild.transform=TextureTransform.Default; other.transform=TextureTransform.Default;
            farChild.transform.position=new Double2(1280,0);
            other.transform.position=new Double2(128,0);
            var state=New(group,other);
            Check(Frame(state).scale==new Double2(2,1),"Bounds exclude unselected descendants");
            var childLocal=farChild.transform;
            Call(state,"Begin"); var start=Frame(state);var next=start;next.position+=new Double2(16,8);
            var oldGroup=World(group);var oldOther=World(other);
            Check((bool)Call(state,"Apply",next,false),"Translate");
            Same(World(group),ProjectiveMatrix.Translate(.125,.0625)*oldGroup,"Group delta");
            Same(World(other),ProjectiveMatrix.Translate(.125,.0625)*oldOther,"Other delta");
            Check(farChild.transform.Equals(childLocal),"Unselected child local untouched");
            Check((bool)Call(state,"Matches",doc,Ids(group,other)),"Own change preserves frame");
            Call(state,"Cancel"); Same(World(group),oldGroup,"Cancel");

            state=New(group,farChild,other);
            Check(Frame(state).scale.x>=11,"Explicitly selected child participates in bounds");
            Call(state,"Begin");start=Frame(state);start.ToMatrix(128,128).TryInverse(out var inv);
            oldGroup=World(group);var oldChild=World(farChild);oldOther=World(other);
            next=start;
            var delta=ProjectiveMatrix.Translate(.03,.02)*ProjectiveMatrix.Rotate(12)*ProjectiveMatrix.Scale(.8,1.1);
            delta.m20=.01;delta.m21=.02;
            Check(next.TrySetMatrix(delta*start.ToMatrix(128,128)),"Perspective frame");
            for(int i=0;i<50;i++)Check((bool)Call(state,"Apply",next,false),"Repeat from immutable snapshot");
            Same(World(group),delta*oldGroup,"Parent perspective");
            Same(World(farChild),delta*oldChild,"Child transforms once");
            Same(World(other),delta*oldOther,"Sibling perspective");
            Check(farChild.transform.Equals(childLocal),"Selected child under selected ancestor keeps local");
            Call(state,"Cancel");

            Layer secondGroup=new GroupLayerBehaviour(), nested=new ShapeLayerBehaviour();
            doc.layers.Add(secondGroup); secondGroup.children.Add(nested);
            secondGroup.transform.rotation=-21; secondGroup.transform.scale=new Double2(.6,.8);
            state=New(farChild,nested); Call(state,"Begin");start=Frame(state);
            var beforeA=World(farChild);var beforeB=World(nested);
            var parentLocal=secondGroup.transform;
            next=start;next.position+=new Double2(-10,15);
            Check((bool)Call(state,"Apply",next,false),"Different parent spaces");
            Same(World(farChild),ProjectiveMatrix.Translate(-10/128d,15/128d)*beforeA,"First local space");
            Same(World(nested),ProjectiveMatrix.Translate(-10/128d,15/128d)*beforeB,"Second local space");
            Check(secondGroup.transform.Equals(parentLocal),"Unselected parent untouched");
            Call(state,"Cancel");
            var localA=farChild.transform;var localB=nested.transform;
            Call(state,"Begin");next=Frame(state);next.pivot=new Double2(.2,.8);
            Check((bool)Call(state,"Apply",next,true),"Shared pivot");
            Check(farChild.transform.Equals(localA)&&nested.transform.Equals(localB),"Pivot does not change layers");
            Call(state,"Cancel");
            Call(state,"Begin");next=Frame(state);next.position+=new Double2(5,6);
            Undo.RegisterCompleteObjectUndo(doc,"Multi transform smoke");
            Call(state,"Apply",next,false);Undo.FlushUndoRecordObjects();Undo.PerformUndo();
            Check(!(bool)Call(state,"Matches",doc,Ids(farChild,nested)),"Undo invalidates cached frame");
            return "PASS: "+checks+" checks: bounds, immutable gesture, different parents, perspective, no double transform, pivot, Cancel and Undo.";
        }
        finally { Undo.ClearUndo(doc); UnityEngine.Object.DestroyImmediate(doc); }
    }
}
