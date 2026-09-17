using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;

public static class GroupTransformSmoke
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    public static string Main()
    {
        checks=0;
        var doc=ScriptableObject.CreateInstance<TextureCompositor>();
        doc.width=64; doc.height=48;
        Layer outer=new GroupLayerBehaviour(), inner=new GroupLayerBehaviour(), leaf=new ShapeLayerBehaviour();
        doc.layers.Add(outer); outer.children.Add(inner); inner.children.Add(leaf);
        outer.transform.position=new Double2(5,-3); outer.transform.rotation=13;
        inner.transform.scale=new Double2(.7,.8);
        leaf.transform.position=new Double2(2,1);
        var local=leaf.transform;
        var refresh=typeof(TextureCompositor).GetMethod("RefreshTransformHierarchy",F);
        var get=typeof(TextureCompositor).GetMethod("GetCanvasTransform",F);
        TextureTransform World(Layer layer)=>(TextureTransform)get.Invoke(doc,new object[]{layer});
        var cacheField=typeof(Layer).GetField("transformCache",F);
        ulong Version(Layer l) { var c=cacheField.GetValue(l); return (ulong)c.GetType().GetField("version",F).GetValue(c); }
        try
        {
            foreach (bool perspective in new[]{false,true})
            {
                if(perspective)
                {
                    var m=outer.transform.ToMatrix(doc.width,doc.height); m.m20=.15; m.m21=.08;
                    Check(outer.transform.TrySetMatrix(m),"set perspective");
                }
                var expected=outer.transform.ToMatrix(64,48)*inner.transform.ToMatrix(64,48)*local.ToMatrix(64,48);
                var world=World(leaf);
                Check(world.ToMatrix(64,48).Equals(expected),"matrix composition");
                Check(leaf.transform.Equals(local),"child local unchanged");
                ulong version=Version(leaf);
                refresh.Invoke(doc,null); Check(Version(leaf)==version,"cache reuse");
                leaf.opacity=.8f; refresh.Invoke(doc,null); Check(Version(leaf)==version,"color-independent cache"); leaf.opacity=1;
                foreach(var mode in new[]{GroupCompositing.PassThrough,GroupCompositing.Isolated})
                {
                    outer.compositing=mode;
                    var nested=doc.Compose();
                    doc.layers.Clear(); doc.layers.Add(leaf); leaf.transform=world;
                    var flat=doc.Compose();
                    try
                    {
                        var a=nested.GetPixels(); var b=flat.GetPixels();
                        for(int i=0;i<a.Length;i++) for(int ch=0;ch<4;ch++)
                            Check(Mathf.Abs(a[i][ch]-b[i][ch])<.006f,"nested render mismatch");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(nested); UnityEngine.Object.DestroyImmediate(flat); }
                    leaf.transform=local; doc.layers.Clear(); doc.layers.Add(outer);
                }
            }
            var nestedCopy=(TextureCompositor)typeof(TextureCompositor).GetMethod("CaptureLayerClipboard",F).Invoke(doc,new object[]{new List<Layer>{leaf}});
            try { Check(nestedCopy.layers[0].transform.ToMatrix(64,48).Equals(World(leaf).ToMatrix(64,48)),"nested clipboard canvas placement"); }
            finally { UnityEngine.Object.DestroyImmediate(nestedCopy); }
            var before=World(leaf).ToMatrix(64,48);
            typeof(TextureCompositor).GetMethod("PreserveTransformForMove",F).Invoke(doc,new object[]{leaf,doc.layers});
            inner.children.Remove(leaf); doc.layers.Add(leaf);
            var after=World(leaf).ToMatrix(64,48);
            for(int i=0;i<10;i++)
            {
                var p=new Double2(i/10d,.3);
                var a=before.Point(p);var b=after.Point(p);
                Check(Math.Abs(a.x-b.x)+Math.Abs(a.y-b.y)<1e-12,"reparent preserves placement");
            }
            var snapshot=(TextureCompositor)typeof(TextureCompositor).GetMethod("CaptureLayerClipboard",F).Invoke(doc,new object[]{new List<Layer>{leaf}});
            try { Check(snapshot.layers[0].transform.ToMatrix(64,48).Equals(World(leaf).ToMatrix(64,48)),"clipboard placement"); }
            finally { UnityEngine.Object.DestroyImmediate(snapshot); }
            ulong oldVersion=Version(inner);
            outer.transform.position=new Double2(3,7); outer.transform.storage=TransformStorage.TRS;
            refresh.Invoke(doc,null); Check(Version(inner)!=oldVersion,"ancestor invalidates descendant");
            var oldLocal=inner.transform;
            UnityEditor.Undo.RegisterCompleteObjectUndo(doc,"Group transform test");
            outer.transform.position=new Double2(20,30);
            UnityEditor.Undo.FlushUndoRecordObjects();
            refresh.Invoke(doc,null);
            Check(inner.transform.Equals(oldLocal),"parent edit keeps child TRS");
            UnityEditor.Undo.PerformUndo();
            outer=doc.layers[0]; inner=outer.children[0]; leaf=doc.layers[1];
            Check(outer.transform.position==new Double2(3,7),"Undo group transform");
            refresh.Invoke(doc,null);
            string json=JsonUtility.ToJson(doc);
            Check(!json.Contains("transformCache") && !json.Contains("parentMatrix"),"cache not serialized");
            return "PASS: "+checks+" checks: nested/projective group renders, isolated/pass-through, cache reuse, local stability and reparent.";
        }
        finally { UnityEngine.Object.DestroyImmediate(doc); }
    }
}
