using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class WhimTexGradientIntegrationSmoke
{
    public sealed class TestHost : EditorWindow
    {
        [SerializeField] private WhimTexGradient gradient = new WhimTexGradient();
        [SerializeField] private WhimTexGradient secondGradient = new WhimTexGradient();
    }
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Field(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
    static object Call(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target,null);
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static void EditColor(WhimTexGradientWindow editor, Color next)
    {
        var field = (ColorField)Field(editor, "color");
        using var change = ChangeEvent<Color>.GetPooled(field.value, next);
        change.target = field;
        field.SetValueWithoutNotify(next);
        field.SendEvent(change);
    }
    public static async Task<string> Main()
    {
        var host=ScriptableObject.CreateInstance<TestHost>();
        WhimTexGradientWindow editor=null;
        string clipboard=EditorGUIUtility.systemCopyBuffer;
        try
        {
            host.Show();
            editor=WhimTexGradientWindow.Open(host,"gradient");
            await Task.Delay(200); // Let the native Editor panel attach before dispatching UI events.
            EditColor(editor, Color.red);
            Check(Mathf.Abs(((WhimTexGradient)Field(host,"gradient")).Evaluate(0).r-1)<.00002f,"Owner not updated: " + ((WhimTexGradient)Field(host,"gradient")).Evaluate(0));
            Check(((WhimTexGradient)Field(host,"secondGradient")).Evaluate(0).r==0,"Other field modified");
            var preview=(Texture2D)Field(editor,"preview");
            uint update=preview.updateCount;
            Call(editor,"Refresh"); Call(editor,"ToggleHdr");
            Check(preview.updateCount==update,"UI-only refresh uploaded preview");
            editor.Close(); editor=null;
            Undo.PerformUndo();
            Check(((WhimTexGradient)Field(host,"gradient")).Evaluate(0).r==0,"Undo after close failed");
            Undo.PerformRedo();
            Check(Mathf.Abs(((WhimTexGradient)Field(host,"gradient")).Evaluate(0).r-1)<.00002f,"Redo after close failed");
            editor=WhimTexGradientWindow.Open(host,"secondGradient");
            await Task.Delay(200);
            EditColor(editor, Color.blue);
            Check(Mathf.Abs(((WhimTexGradient)Field(host,"secondGradient")).Evaluate(0).b-1)<.00002f,"Second binding failed");
            Check(Mathf.Abs(((WhimTexGradient)Field(host,"gradient")).Evaluate(0).r-1)<.00002f,"First binding overwritten");
            string json=EditorJsonUtility.ToJson(host);
            var copy=ScriptableObject.CreateInstance<TestHost>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(json,copy);
                Check(Mathf.Abs(((WhimTexGradient)Field(copy,"gradient")).Evaluate(0).r-1)<.00002f,"Owner serialization failed");
                Check(Mathf.Abs(((WhimTexGradient)Field(copy,"secondGradient")).Evaluate(0).b-1)<.00002f,"Second serialization failed");
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
            var sourceField=new WhimTexGradientField("Source",host,"gradient");
            var destinationField=new WhimTexGradientField("Destination",host,"secondGradient");
            host.rootVisualElement.Add(sourceField); host.rootVisualElement.Add(destinationField);
            sourceField.CopyValue();
            Check(destinationField.PasteValue(),"Clipboard paste failed");
            var source=(WhimTexGradient)Field(host,"gradient");
            var destination=(WhimTexGradient)Field(host,"secondGradient");
            Check(!ReferenceEquals(source,destination),"Clipboard shared gradient instance");
            Check(!ReferenceEquals(Field(source,"colors"),Field(destination,"colors")),"Clipboard shared color array");
            Check(!ReferenceEquals(Field(source,"alphas"),Field(destination,"alphas")),"Clipboard shared alpha array");
            Check(destination.Evaluate(0)==source.Evaluate(0),"Clipboard changed value");
            Undo.PerformUndo();
            Check(Mathf.Abs(((WhimTexGradient)Field(host,"secondGradient")).Evaluate(0).b-1)<.00002f,"Paste Undo failed");
            Undo.PerformRedo();
            Check(Mathf.Abs(((WhimTexGradient)Field(host,"secondGradient")).Evaluate(0).r-1)<.00002f,"Paste Redo failed");
            EditorGUIUtility.systemCopyBuffer="not a gradient";
            Check(!destinationField.PasteValue(),"Invalid clipboard accepted");
            var g=new WhimTexGradient { ColorSpace=ColorSpace.Linear };
            g.SetKeys(new[]{new GradientColorKey(new Color(8,2,1),0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(.25f,0),new GradientAlphaKey(1,1)});
            using(var cache=new WhimTexGradientTexture())
            {
                var texture=cache.GetTexture(g);
                Check(texture.width==512 && texture.height==2 && texture.format==TextureFormat.RGBAHalf,"HDR LUT format");
                Check(texture.GetPixel(0,0).r==8 && texture.GetPixel(0,0).a==.25f,"HDR/alpha lost");
                Check(texture.GetPixel(0,0)==texture.GetPixel(0,1),"LUT rows differ");
                for(int i=0;i<100;i++) Check(cache.GetTexture(g)==texture,"LUT recreated");
                Check(cache.BakeCount==1,"LUT rebuilt without edit");
                g.Smoothness=.25f; cache.GetTexture(g);
                Check(cache.BakeCount==2,"Smoothness did not invalidate LUT");
                g.Mode=WhimTexGradientMode.Fixed; cache.GetTexture(g);
                Check(texture.filterMode==FilterMode.Point,"Fixed uses interpolated sampler");
                cache.Dispose(); Check(texture==null,"LUT not destroyed");
            }
            return "Bindings, independent fields, Undo/Redo after close, serialization, UI cache and HDR LUT lifecycle passed.";
        }
        finally
        {
            EditorGUIUtility.systemCopyBuffer=clipboard;
            if(editor!=null)editor.Close();
            Undo.ClearUndo(host); host.Close();
        }
    }
}
