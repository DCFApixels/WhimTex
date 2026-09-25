using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using DCFApixels.WhimTex;

public static class FillPatternSmoke
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static string Main()
    {
        AssetDatabase.ImportAsset("Packages/com.dcfapixels.whimtex/src/Shaders/FillPattern.shader", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var doc = ScriptableObject.CreateInstance<TextureCompositor>();
        doc.width = 127; doc.height = 91;
        var fill = new ColorFillLayerBehaviour { mode = ColorFillLayerBehaviour.FillMode.Pattern };
        Layer layer = fill; doc.layers.Add(layer);
        var material = new Material(Shader.Find("Hidden/TextureCompositor/FillPattern"));
        var output = RenderTexture.GetTemporary(127, 91, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(127, 91, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        Texture2D sheet = null;
        string folder = null;
        TextureCompositor loaded = null;
        ShaderFX effect = null;
        FillPatternSmokeWindow window = null;
        int checks = 0;
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; }
        Color[] Render()
        {
            GL.sRGBWrite = false; Graphics.Blit(null, output, material);
            RenderTexture.active = output;
            read.ReadPixels(new Rect(0,0,127,91),0,0); read.Apply();
            return read.GetPixels();
        }
        // Half-float gradient LUT and GPU texture filtering have sub-8-bit rounding error.
        void Same(Color[] a, Color[] b, string text, float tolerance = .002f)
        {
            for (int i=0;i<a.Length;i++) for(int c=0;c<4;c++)
            {
                if(float.IsNaN(b[i][c]) || float.IsInfinity(b[i][c]) || Mathf.Abs(a[i][c]-b[i][c]) >= tolerance)
                    throw new Exception(text + " at " + i + "/" + c + ": " + a[i] + " / " + b[i]);
                checks++;
            }
        }
        var assembly = typeof(ShaderFX).Assembly;
        object context = Activator.CreateInstance(assembly.GetType("DCFApixels.WhimTex.LayerRenderContext"),
            doc, null, 127, 91, 1f, true, true, null);
        void Prepare()
        {
            typeof(TextureCompositor).GetMethod("RefreshTransformHierarchy", F).Invoke(doc, null);
            typeof(FillPatternSettings).GetMethod("Prepare", F).Invoke(fill.pattern, new[] { material, (object)layer, context });
        }
        try
        {
            fill.pattern.size = 29;
            Check(fill.pattern.Size == new Vector2(29,29), "Legacy scalar inherits both axes");
            var legacy = JsonUtility.FromJson<FillPatternSettings>("{\"size\":37}");
            Check(legacy.Size == new Vector2(37,37), "Legacy serialized scalar inherits both axes");
            legacy.Dispose();
            fill.pattern.Size = new Vector2(29,17); fill.pattern.seamless = true;
            fill.pattern.offset = new Vector2(13.2f,-7.3f);
            var transform = layer.transform;
            transform.rotation = 31; transform.scale = new Double2(1.3,.8);
            layer.transform = transform;
            sheet = new Texture2D(127*5,91*3,TextureFormat.RGBA32,false,true);
            for (int kind=0;kind<5;kind++)
            {
                fill.pattern.shape = (FillPatternSettings.Shape)Math.Min(kind,3);
                fill.pattern.circleLayout = kind==4 ? FillPatternSettings.CircleLayout.Dense : FillPatternSettings.CircleLayout.Square;
                for(int variant=0;variant<3;variant++)
                {
                    fill.pattern.roundness = variant==0 ? 0 : variant==1 ? .4f : 1;
                    fill.pattern.gap = variant==0 ? 0 : .12f;
                    fill.pattern.bulge = variant==2 ? .6f : 0;
                    Prepare();
                    var original = Render();
                    var row0=material.GetVector("_PatternRow0"); var row1=material.GetVector("_PatternRow1");
                    for(int axis=0;axis<2;axis++)
                    {
                        var x=row0;var y=row1; x.z+=row0[axis];y.z+=row1[axis];
                        material.SetVector("_PatternRow0",x);material.SetVector("_PatternRow1",y);
                        Same(original,Render(),"Periodic "+kind+"/"+variant+"/"+axis);
                    }
                    for(int y=0;y<91;y++)for(int x=0;x<127;x++)
                    {
                        var c=original[y*127+x]; c.r=Mathf.LinearToGammaSpace(c.r);c.g=c.r;c.b=c.r;
                        sheet.SetPixel(kind*127+x, (2-variant)*91+y,c);
                    }
                    // Direct model roundtrip includes curves and gradient tracks.
                    var copy=JsonUtility.FromJson<FillPatternSettings>(JsonUtility.ToJson(fill.pattern));
                    Check(copy.shape==fill.pattern.shape && copy.roundness==fill.pattern.roundness &&
                        copy.Size == fill.pattern.Size && copy.linkSize == fill.pattern.linkSize &&
                        copy.gradient.Equals(fill.pattern.gradient) && copy.profile.Equals(fill.pattern.profile), "Model roundtrip");
                    copy.Dispose();
                }
            }
            foreach(var message in ShaderUtil.GetShaderMessages(material.shader))
                Check(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error &&
                    message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Warning, message.message);
            sheet.Apply(); Directory.CreateDirectory("Temp/WhimTex");
            File.WriteAllBytes("Temp/WhimTex/FillPatternSmoke.png",sheet.EncodeToPNG());

            fill.pattern.shape=FillPatternSettings.Shape.Hexagons;
            fill.pattern.seamless=false;fill.pattern.rotation=13.5f; Prepare();
            var free=Render();
            fill.pattern.rotation=24.5f;Prepare();
            var changed=Render();float delta=0;
            for(int i=0;i<free.Length;i++)delta+=Mathf.Abs(free[i].r-changed[i].r);
            Check(delta/free.Length>.01f,"Free rotation changes result");

            fill.pattern.palette.SetKeys(new[] { new GradientColorKey(Color.red,0),
                new GradientColorKey(Color.green,.5f),new GradientColorKey(Color.blue,1) },
                new[] { new GradientAlphaKey(0,0),new GradientAlphaKey(0,1) });
            fill.pattern.colorBlend=FillPatternSettings.ColorBlend.ReplaceRGB;
            fill.pattern.seed=-1234567;fill.pattern.seamless=true;
            for(int kind=0;kind<5;kind++)
            {
                fill.pattern.shape=(FillPatternSettings.Shape)Math.Min(kind,3);
                fill.pattern.circleLayout=kind==4?FillPatternSettings.CircleLayout.Dense:FillPatternSettings.CircleLayout.Square;
                for(int mode=1;mode<=2;mode++)
                {
                    fill.pattern.cellColor=(FillPatternSettings.CellColor)mode;
                    Prepare();var colored=Render();
                    var rx=material.GetVector("_PatternRow0");var ry=material.GetVector("_PatternRow1");
                    for(int axis=0;axis<2;axis++)
                    {
                        var x=rx;var y=ry;x.z-=rx[axis];y.z-=ry[axis];
                        material.SetVector("_PatternRow0",x);material.SetVector("_PatternRow1",y);
                        Same(colored,Render(),"Color periodicity "+kind+"/"+mode+"/"+axis);
                    }
                    Prepare();Same(colored,Render(),"Deterministic colors");
                    for(int y=0;y<91;y++)for(int x=0;x<127;x++)
                        sheet.SetPixel(kind*127+x,(mode-1)*91+y,colored[y*127+x].gamma);
                    Check(colored[0].a>.99f,"Palette alpha does not replace SDF alpha");
                    if(mode==2)
                    {
                        var colors=new System.Collections.Generic.HashSet<int>();
                        foreach(var c in colored)colors.Add(Mathf.RoundToInt(c.r*255)*65536+Mathf.RoundToInt(c.g*255)*256+Mathf.RoundToInt(c.b*255));
                        Check(colors.Count==(kind==2||kind==4?3:2),"Regular palette color count "+kind+": "+colors.Count);
                        Color At(float x,float y)
                        {
                            material.SetVector("_PatternRow0",new Vector4(0,0,x,0));
                            material.SetVector("_PatternRow1",new Vector4(0,0,y,0));
                            return Render()[0];
                        }
                        if(kind==0) Check(At(.5f,.28867513f)!=At(0,.57735027f),"Triangle orientations differ");
                        else
                        {
                            var center=At(0,0);var right=At(1,0);
                            var nextRow=At(kind==2||kind==4?.5f:0,kind==2||kind==4?.8660254f:1);
                            Check(center!=right&&center!=nextRow,"Neighbors differ "+kind);
                            if(kind==2||kind==4)Check(right!=nextRow,"Staggered neighbors use third color");
                        }
                    }
                }
            }
            sheet.Apply();File.WriteAllBytes("Temp/WhimTex/FillPatternColors.png",sheet.EncodeToPNG());
            fill.pattern.shape=FillPatternSettings.Shape.Hexagons;
            fill.pattern.cellColor=FillPatternSettings.CellColor.Random;
            Prepare();var seeded=Render();fill.pattern.seed++;Prepare();
            var reseeded=Render();delta=0;
            for(int i=0;i<seeded.Length;i++)delta+=Mathf.Abs(seeded[i].r-reseeded[i].r);
            Check(delta>1,"Seed changes colors");
            fill.pattern.seed+=16777216;Prepare();var highSeed=Render();delta=0;
            for(int i=0;i<seeded.Length;i++)delta+=Mathf.Abs(highSeed[i].r-reseeded[i].r);
            Check(delta>1,"Full integer seed reaches shader");
            fill.pattern.variation=0;Prepare();var flat=Render();
            foreach(var c in flat)Check(Mathf.Abs(c.r-flat[0].r)<.002f&&Mathf.Abs(c.g-flat[0].g)<.002f,"Zero variation is uniform");
            fill.pattern.variation=.8f;Prepare();var tint=Render();
            fill.pattern.cellColor=FillPatternSettings.CellColor.Uniform;Prepare();var distance=Render();
            fill.pattern.cellColor=FillPatternSettings.CellColor.Random;
            fill.pattern.colorBlend=FillPatternSettings.ColorBlend.Multiply;Prepare();var multiply=Render();
            for(int i=0;i<tint.Length;i++)for(int c=0;c<3;c++)
                Check(Mathf.Abs(multiply[i][c]-distance[i][c]*tint[i][c])<.002f,"Linear Multiply");

            var api=typeof(WhimTexApi);
            var snapshot=api.GetMethod("FillPatternSnapshot",F);
            var setter=api.GetMethod("SetFillPattern",F);
            var roundtrip=new FillPatternSettings();
            setter.Invoke(null,new[]{(object)roundtrip,snapshot.Invoke(null,new[]{(object)fill.pattern})});
            Check(snapshot.Invoke(null,new[]{(object)roundtrip}).ToString()==snapshot.Invoke(null,new[]{(object)fill.pattern}).ToString(),"API roundtrip");
            roundtrip.Dispose();
            Check(WhimTexApi.Describe().Contains("fillPatternDefaults"),"Discovery");

            fill.pattern.seamless=true;
            Layer group = new GroupLayerBehaviour(); group.children.Add(layer);
            var groupTransform = group.transform;
            groupTransform.rotation=57;groupTransform.scale=new Double2(.8,1.2);group.transform=groupTransform;
            doc.layers.Clear();doc.layers.Add(group);
            Prepare();var grouped=Render();
            var gx=material.GetVector("_PatternRow0");var gy=material.GetVector("_PatternRow1");
            var shiftedX=gx;var shiftedY=gy;shiftedX.z+=gx.x;shiftedY.z+=gy.x;
            material.SetVector("_PatternRow0",shiftedX);material.SetVector("_PatternRow1",shiftedY);
            Same(grouped,Render(),"Group transform periodicity");
            var groupImage=doc.Compose();
            try { Check(groupImage!=null,"Group composite"); }
            finally { UnityEngine.Object.DestroyImmediate(groupImage); }
            doc.layers.Clear();doc.layers.Add(layer);
            Texture2D composite=doc.Compose();
            try { Check(composite!=null && composite.width==127,"Composite"); }
            finally { UnityEngine.Object.DestroyImmediate(composite); }
            Check(fill.GetPreviewTexture(32)!=null,"Thumbnail");
            var thumb=fill.GetPreviewTexture(32);fill.pattern.roundness=.13f;
            Check(fill.GetPreviewTexture(32)!=thumb,"Thumbnail invalidation");
            thumb=fill.GetPreviewTexture(32);fill.pattern.seed++;
            Check(fill.GetPreviewTexture(32)!=thumb,"Color thumbnail invalidation");
            // Actual TIFF save/reopen, isolated from user assets.
            folder = "Assets/WhimTexPatternSmoke_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            string saved = WhimTexDocumentFile.Save(doc, folder + "/pattern.tiff");
            loaded = WhimTexDocumentFile.Load(saved);
            var restored = (ColorFillLayerBehaviour)loaded.layers[0].Behaviour;
            Check(restored.mode == fill.mode && JsonUtility.ToJson(restored.pattern) == JsonUtility.ToJson(fill.pattern), "TIFF settings roundtrip");
            var before = doc.Compose(); var after = loaded.Compose();
            try { Same(before.GetPixels(), after.GetPixels(), "TIFF pixels"); }
            finally { UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after); }
            var portable = (string)api.GetMethod("WritePortableClipboard",F).Invoke(null,new object[]{doc,new System.Collections.Generic.List<Layer>{layer}});
            var imported = (IDisposable)api.GetMethod("ReadProceduralClipboard",F).Invoke(null,new object[]{portable,127,91});
            try
            {
                var pasted = (TextureCompositor)imported.GetType().GetField("Document",F).GetValue(imported);
                var pastedFill = (ColorFillLayerBehaviour)pasted.layers[0].Behaviour;
                Check(JsonUtility.ToJson(pastedFill.pattern)==JsonUtility.ToJson(fill.pattern),"Portable settings roundtrip");
            }
            finally { imported.Dispose(); }
            // Same FX must work on the layer and on an isolated group containing it.
            var draft=typeof(ShaderFX).GetMethod("CreateAgentDraft",F,null,
                new[]{typeof(TextureCompositor),typeof(string),typeof(System.Collections.Generic.List<ShaderFXParameter>)},null);
            effect=(ShaderFX)draft.Invoke(null,new object[]{doc,
                "float4 ApplyFX(float2 uv,float4 color){ return float4(1-color.rgb,color.a); }",
                new System.Collections.Generic.List<ShaderFXParameter>()});
            typeof(ShaderFX).GetMethod("ApplyAgentDraft",F).Invoke(effect,null);
            layer.modifiers.Add(effect);
            var withFx=doc.Compose();
            layer.modifiers.Clear();
            group.modifiers.Add(effect);
            doc.layers.Clear();doc.layers.Add(group);
            group.transform=TextureTransform.Default;
            var groupFx=doc.Compose();
            try { Same(withFx.GetPixels(),groupFx.GetPixels(),"Layer/group FX",.006f); }
            finally { UnityEngine.Object.DestroyImmediate(withFx);UnityEngine.Object.DestroyImmediate(groupFx); }
            var exported=(Texture2D)typeof(TextureCompositor).GetMethod("RenderPsdGroupContent",F).Invoke(doc,new object[]{group});
            try { Check(exported!=null && exported.width==127,"Layered group export"); }
            finally { UnityEngine.Object.DestroyImmediate(exported); }
            group.modifiers.Clear();doc.layers.Clear();doc.layers.Add(layer);
            layer.clippingMask=true;
            doc.layers.Add(new ColorFillLayerBehaviour { color=new Color(0,0,0,.5f) });
            var clipped=doc.Compose();
            try { Check(Mathf.Abs(clipped.GetPixel(63,45).a-.5f)<.01f,"Clipping preserves base alpha"); }
            finally { UnityEngine.Object.DestroyImmediate(clipped); }
            layer.clippingMask=false;doc.layers.RemoveAt(1);
            var savedTransform=layer.transform;layer.transform=TextureTransform.Default;
            fill.mode=ColorFillLayerBehaviour.FillMode.Color;fill.color=new Color(.25f,.5f,.75f,1);
            var solid=doc.Compose();
            try { Check(Mathf.Abs(solid.GetPixel(63,45).r-Mathf.GammaToLinearSpace(.25f))<.01f,"Existing Color mode"); }
            finally { UnityEngine.Object.DestroyImmediate(solid); }
            fill.mode=ColorFillLayerBehaviour.FillMode.UV;
            var uvImage=doc.Compose();
            try { Check(Mathf.Abs(uvImage.GetPixel(63,45).r-.5f)<.01f,"Existing UV mode"); }
            finally { UnityEngine.Object.DestroyImmediate(uvImage); }
            fill.mode=ColorFillLayerBehaviour.FillMode.Pattern;layer.transform=savedTransform;
            // Own temporary panel: UI Toolkit dispatches value events only on an attached tree.
            var ui=assembly.GetType("DCFApixels.WhimTex.WhimTexUI");
            var bindingType=ui.GetNestedType("ValueBindings",F);
            var bindings=Activator.CreateInstance(bindingType,true);
            var root=new VisualElement();
            window=ScriptableObject.CreateInstance<FillPatternSmokeWindow>();
            window.titleContent=new GUIContent("Pattern test");
            window.ShowUtility();window.rootVisualElement.Add(root);
            Action<string,Action> change=(label,action)=>action();
            typeof(ColorFillLayerEditorWindow).GetMethod("BuildFields",F).Invoke(null,new object[]{root,fill,doc,change,bindings});
            void Refresh()=>bindingType.GetMethod("Refresh",F).Invoke(bindings,new object[]{true});
            Refresh();
            void Edit<T>(BaseField<T> field,T value)
            {
                using var evt=ChangeEvent<T>.GetPooled(field.value,value);
                field.SetValueWithoutNotify(value);evt.target=field;field.SendEvent(evt);
            }
            var shapeField=root.Query<EnumField>().ToList().Find(f=>f.label=="Shape");
            var colorMode=root.Query<EnumField>().ToList().Find(f=>f.label=="Cell Color");
            var seedField=root.Query<IntegerField>().ToList().Find(f=>f.label=="Seed");
            Edit<Enum>(colorMode,FillPatternSettings.CellColor.Uniform);Refresh();
            Check(seedField.ClassListContains("whimtex-hidden"),"Uniform hides random controls");
            Edit<Enum>(colorMode,FillPatternSettings.CellColor.Random);Refresh();
            Check(!seedField.ClassListContains("whimtex-hidden"),"Random shows seed");
            Edit(seedField,951);Check(fill.pattern.seed==951,"Seed editing");
            Edit<Enum>(colorMode,FillPatternSettings.CellColor.Pattern);Refresh();
            Check(seedField.ClassListContains("whimtex-hidden"),"Pattern hides seed");
            var sizeField=root.Query<Vector2Field>().ToList().Find(f=>f.label=="Size (px)");
            var link=root.Q<Button>("linkSize");
            Check(link.parent[0]==link && link.parent==sizeField.Q(className:"unity-base-field__input"),"Link is left of vector inputs");
            fill.pattern.Size=new Vector2(40,20);fill.pattern.linkSize=true;Refresh();
            Edit(sizeField,new Vector2(80,20));
            Check(fill.pattern.Size==new Vector2(80,40),"Linked X scales both axes");
            Edit(sizeField,new Vector2(80,20));
            Check(fill.pattern.Size==new Vector2(40,20),"Linked Y scales both axes");
            using(var evt=NavigationSubmitEvent.GetPooled()) { evt.target=link;link.SendEvent(evt); }
            Check(!fill.pattern.linkSize && fill.pattern.Size==new Vector2(40,20),"Unlink preserves dimensions");
            Edit(sizeField,new Vector2(70,20));
            Check(fill.pattern.Size==new Vector2(70,20),"Unlinked X is independent");
            Edit(sizeField,new Vector2(70,30));
            Check(fill.pattern.Size==new Vector2(70,30),"Unlinked Y is independent");
            using(var evt=NavigationSubmitEvent.GetPooled()) { evt.target=link;link.SendEvent(evt); }
            Check(fill.pattern.linkSize && fill.pattern.Size==new Vector2(70,30),"Relink preserves dimensions");
            fill.pattern.Size=new Vector2(16000,8000);Refresh();
            Edit(sizeField,new Vector2(16000,16000));
            Check(fill.pattern.Size==new Vector2(16384,8192),"Linked upper bound preserves ratio");
            Edit<Enum>(shapeField,FillPatternSettings.Shape.Circles);Refresh();
            Check(fill.pattern.shape==FillPatternSettings.Shape.Circles,"Inspector shape edits model");
            var roundness=root.Query<Slider>().ToList().Find(f=>f.label=="Roundness");
            Check(roundness.parent.ClassListContains("whimtex-hidden"),"Circles hide polygon rounding");
            Edit<Enum>(shapeField,FillPatternSettings.Shape.Hexagons);Refresh();
            Check(!roundness.parent.ClassListContains("whimtex-hidden"),"Polygons show rounding");
            var rotation=root.Query<FloatField>().ToList().Find(f=>f.label=="Rotation");
            Edit(rotation,37f);
            Check(fill.pattern.rotation==0,"Seamless inspector snaps rotation");
            Edit(root.Query<Toggle>().ToList().Find(f=>f.label=="Seamless"),false);
            Edit(rotation,37f);
            Check(fill.pattern.rotation==37,"Free inspector preserves rotation");
            // Undo only touches the test document and clears only its own records.
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(doc,"Pattern smoke");
            fill.pattern.roundness=.77f;
            fill.pattern.Size=new Vector2(60,25);fill.pattern.linkSize=false;
            fill.pattern.cellColor=FillPatternSettings.CellColor.Uniform;
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(((ColorFillLayerBehaviour)doc.layers[0].Behaviour).pattern.roundness==.13f,"Undo");
            var undoPattern=((ColorFillLayerBehaviour)doc.layers[0].Behaviour).pattern;
            Check(undoPattern.Size==new Vector2(16384,8192) && undoPattern.linkSize,"Undo dimensions and link");
            Check(undoPattern.cellColor==FillPatternSettings.CellColor.Pattern,"Undo colors");
            Undo.PerformRedo();
            Check(((ColorFillLayerBehaviour)doc.layers[0].Behaviour).pattern.roundness==.77f,"Redo");
            var redoPattern=((ColorFillLayerBehaviour)doc.layers[0].Behaviour).pattern;
            Check(redoPattern.Size==new Vector2(60,25) && !redoPattern.linkSize,"Redo dimensions and link");
            Check(redoPattern.cellColor==FillPatternSettings.CellColor.Uniform,"Redo colors");
            Undo.ClearUndo(doc); Undo.IncrementCurrentGroup();
            return "PASS FillPatternSmoke: "+checks+" checks; all five layouts periodic on GPU, roundness/bulge, layer/group transforms, TIFF pixels/settings, portable/API roundtrip, Undo/Redo, free rotation, composite and thumbnails; preview Temp/WhimTex/FillPatternSmoke.png";
        }
        finally
        {
            RenderTexture.active=previous; GL.sRGBWrite=srgb;
            RenderTexture.ReleaseTemporary(output);
            UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(material);
            if(sheet!=null)UnityEngine.Object.DestroyImmediate(sheet);
            if(loaded!=null)UnityEngine.Object.DestroyImmediate(loaded);
            if(effect!=null)UnityEngine.Object.DestroyImmediate(effect);
            if(window!=null)window.Close();
            UnityEngine.Object.DestroyImmediate(doc);
            if(folder!=null)AssetDatabase.DeleteAsset(folder);
        }
    }
}
public sealed class FillPatternSmokeWindow : EditorWindow { }
