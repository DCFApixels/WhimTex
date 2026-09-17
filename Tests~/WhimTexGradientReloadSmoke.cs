using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;

public static class WhimTexGradientReloadSmoke
{
    const string Key="WhimTex.GradientReloadSmoke.Id";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Begin()
    {
        var host=ScriptableObject.CreateInstance<TextureCompositor>();
        var g=new WhimTexGradient();
        g.SetKeys(new[]{new GradientColorKey(new Color(4,2,1),0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(.3f,0),new GradientAlphaKey(1,1)});
        g.SetMidpoint(false,0,.23f);
        host.layers.Add(new GradientLayerBehaviour { gradient = g });
        string title="Gradient Reload Test "+Guid.NewGuid().ToString("N");
        host.name=title; host.hideFlags=HideFlags.HideAndDontSave;
        SessionState.SetString(Key,title);
        return "Temporary gradient document ready for Unity recompilation.";
    }
    public static string End()
    {
        TextureCompositor host=null;
        string title=SessionState.GetString(Key,"");
        foreach(var candidate in Resources.FindObjectsOfTypeAll<TextureCompositor>())
            if(candidate.name==title) { host=candidate; break; }
        if(host==null)throw new Exception("Reload test document not restored");
        try
        {
            var g=((GradientLayerBehaviour)host.layers[0].Behaviour).gradient;
            if(g.Evaluate(0).r!=4 || Mathf.Abs(g.Evaluate(0).a-.3f)>.00001f || g.GetMidpoint(false,0)!=.23f)
                throw new Exception("HDR, alpha or midpoint lost after reload");
            return "Actual Unity domain reload preserved HDR, alpha and midpoint.";
        }
        finally { UnityEngine.Object.DestroyImmediate(host); SessionState.EraseString(Key); }
    }
}
