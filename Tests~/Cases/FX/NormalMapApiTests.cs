using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
using Object = UnityEngine.Object;

// Independent port: complete original body, assertion inputs and finally cleanup retained.
public static class NormalMapApiTests
{
    public static string Run() => WhimTex.Tests.UnityC.FixtureContext.Run("NormalMapApiTests", Body);
    static void Body()
    {
        // Opt-in after manual compilation. No saves/imports, rendering or Undo operations.
        var type = typeof(DCFApixels.WhimTex.WhimTexApi);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var setter = type.GetMethod("SetNormalMap", flags);
        var snapshot = type.GetMethod("NormalMapSnapshot", flags);
        var jsonType = setter.GetParameters()[1].ParameterType;
        object Json(string text) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
        var normal = new DCFApixels.WhimTex.NormalMapLayerBehaviour();
        int checks = 0;
        void Check(bool value, string message) { WhimTex.Tests.UnityC.FixtureContext.Context.True(value, message); checks++; }
        void Set(string json) => setter.Invoke(null, new object[] { normal, Json(json) });
        void Reject(string json)
        {
            bool rejected = false;
            try { Set(json); }
            catch (System.Reflection.TargetInvocationException e) { rejected = e.InnerException?.GetType().Name == "WhimTexApiException"; }
            Check(rejected, "Reject " + json);
        }
        Set("{\"mode\":\"Texture\",\"sourceChannel\":\"Alpha\",\"inputSpace\":\"Linear\",\"edges\":\"Repeat\",\"encoding\":\"LinearData\",\"strength\":12,\"flipY\":true}");
        Check(normal.mode == DCFApixels.WhimTex.NormalMapLayerBehaviour.GenerationMode.Texture && normal.flipY && normal.strength == 12, "Apply settings");
        var copy = new DCFApixels.WhimTex.NormalMapLayerBehaviour();
        setter.Invoke(null, new object[] { copy, snapshot.Invoke(null, new object[] { normal }) });
        Check(snapshot.Invoke(null, new object[]{normal}).ToString() == snapshot.Invoke(null, new object[]{copy}).ToString(), "Complete API settings round-trip (wrapper identity is not a normal-map setting)");
        Reject("{\"strength\":-1}"); Reject("{\"edges\":\"Unknown\"}"); Reject("{\"unused\":1}");
        Reject("{\"blackLevel\":1,\"whiteLevel\":0.5}"); Set("{\"blackLevel\":0,\"whiteLevel\":1}");
        Reject("{\"mediumRadius\":32,\"largeRadius\":2}"); Set("{\"mediumRadius\":4,\"largeRadius\":32}");
        Check(DCFApixels.WhimTex.WhimTexApi.Describe().Contains("normalMapDefaults"), "Describe advertises defaults");
        return;
        
    }
}
