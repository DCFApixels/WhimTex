using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
public static class WhimTexGradientProductSmoke
{
    public static string Main()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var host = ScriptableObject.CreateInstance<EditorWindow>();
        WhimTexGradientWindow editor = null;
        try
        {
            host.Show();
            var field = new WhimTexGradientValueField("Gradient");
            host.rootVisualElement.Add(field);
            var original = new WhimTexGradient();
            field.SetValueWithoutNotify(original);
            int changes = 0;
            field.RegisterValueChangedCallback(e => changes++);
            field.GetType().GetMethod("OpenEditor", flags).Invoke(field, null);
            editor = Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>()[0];
            ((ColorField)editor.GetType().GetField("color", flags).GetValue(editor)).value = Color.red;
            if (changes != 1 || field.value.Evaluate(0).r != 1 || original.Evaluate(0).r != 0)
                throw new Exception("Product field callback/deep copy failed.");
            editor.Close(); editor = null;
            var read = typeof(WhimTexApi).GetMethod("ReadGradient", BindingFlags.Static | BindingFlags.NonPublic);
            var jsonType = read.GetParameters()[0].ParameterType.Assembly.GetType("Newtonsoft.Json.Linq.JObject");
            var json = jsonType.GetMethod("Parse", new[]{typeof(string)}).Invoke(null, new object[]{"{colors:[{time:0,color:[0,0,0,1],midpoint:0.2},{time:1,color:[1,1,1,1]}],alphas:[{time:0,alpha:1},{time:1,alpha:0}],mode:'Perceptual',smoothness:0.6}"});
            var gradient = (WhimTexGradient)read.Invoke(null,new object[]{json,WhimTexGradientMode.Classic});
            if(gradient.Mode!=WhimTexGradientMode.Perceptual || gradient.Smoothness!=.6f || gradient.GetMidpoint(false,0)!=.2f)
                throw new Exception("JSON gradient settings lost.");
            if(new SDFLayerBehaviour().gradient.Mode!=WhimTexGradientMode.Linear)
                throw new Exception("SDF default must be Linear.");
            return "Product field edits, independent copy, JSON mode/smoothness/midpoint and SDF default passed.";
        }
        finally { if(editor!=null)editor.Close(); host.Close(); }
    }
}
