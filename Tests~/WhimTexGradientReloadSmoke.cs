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
        if (SessionState.GetString(Key, "").Length != 0) throw new Exception("Finish the previous gradient reload test first.");
        var host=ScriptableObject.CreateInstance<TextureCompositor>();
        var g=new WhimTexGradient();
        g.SetKeys(new[]{new GradientColorKey(new Color(4,2,1),0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(.3f,0),new GradientAlphaKey(1,1)});
        g.SetMidpoint(false,0,.23f);
        host.layers.Add(new GradientLayerBehaviour { gradient = g });
        string title="Gradient Reload Test "+Guid.NewGuid().ToString("N");
        host.name=title; host.hideFlags=HideFlags.HideAndDontSave;
        // Unity restores EditorWindow-owned documents; an unowned transient object is not a reload fixture.
        var window = EditorWindow.CreateWindow<TextureCompositorWindow>();
        typeof(TextureCompositorWindow).GetMethod("SetCompositor", Flags).Invoke(window, new object[] {host});
        window.Show();
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
            // Compare stored HDR keys: Perceptual evaluation is not an exact RGB identity transform.
            if(g.ColorKeys[0].color.r!=4 || g.AlphaKeys[0].alpha!=.3f || g.GetMidpoint(false,0)!=.23f)
                throw new Exception("HDR, alpha or midpoint lost after reload: " + JsonUtility.ToJson(g));
            return "Actual Unity domain reload preserved HDR, alpha and midpoint.";
        }
        finally
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (ReferenceEquals(typeof(TextureCompositorWindow).GetField("compositor", Flags).GetValue(window), host))
                { window.DiscardChanges(); window.Close(); }
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            SessionState.EraseString(Key);
        }
    }
}
