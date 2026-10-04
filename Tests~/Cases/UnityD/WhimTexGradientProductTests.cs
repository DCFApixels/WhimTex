// Independent migrated assertions; compiled and executed only by the parent runner.
using WhimTex.Tests;
using WhimTex.Tests.UnityD;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;
using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;
public static class WhimTexGradientProductTests
{
    static TestContext context;
    static MigrationD fixture;

    public static string Start(string runId) => AsyncD.Start(runId, ExecuteMain);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    private static async Task ExecuteMain(TestContext runContext, System.Threading.CancellationToken token)
    {
        context = runContext;
        context.True(Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>().Length == 0,
            "Close the existing gradient editor before running this singleton-window test");
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
            var ownedSession = field.GetType().GetField("session", flags).GetValue(field);
            foreach (var candidate in Resources.FindObjectsOfTypeAll<WhimTexGradientWindow>())
                if (ReferenceEquals(candidate.GetType().GetField("owner", flags).GetValue(candidate), ownedSession)) editor = candidate;
            context.True(editor != null, "Value field opened its owned gradient session");
            await Task.Delay(200, token); // Let the native Editor panel attach before dispatching UI events.
            var colorField = (ColorField)editor.GetType().GetField("color", flags).GetValue(editor);
            using (var change = ChangeEvent<Color>.GetPooled(colorField.value, Color.red))
            {
                change.target = colorField;
                colorField.SetValueWithoutNotify(Color.red);
                colorField.SendEvent(change);
            }
            context.True(!(changes != 1 || Mathf.Abs(field.value.Evaluate(0).r - 1) > .00002f || original.Evaluate(0).r != 0), "Product field callback/deep copy failed: changes=" + changes + ", edited=" + field.value.Evaluate(0) + ", original=" + original.Evaluate(0));
            editor.Close(); editor = null;
            var read = typeof(WhimTexApi).GetMethod("ReadGradient", BindingFlags.Static | BindingFlags.NonPublic);
            var jsonType = read.GetParameters()[0].ParameterType.Assembly.GetType("Newtonsoft.Json.Linq.JObject");
            var json = jsonType.GetMethod("Parse", new[]{typeof(string)}).Invoke(null, new object[]{"{colors:[{time:0,color:[0,0,0,1],midpoint:0.2},{time:1,color:[1,1,1,1]}],alphas:[{time:0,alpha:1},{time:1,alpha:0}],mode:'Perceptual',smoothness:0.6}"});
            var gradient = (WhimTexGradient)read.Invoke(null,new object[]{json,WhimTexGradientMode.Classic,107f});
            context.True(!(gradient.Mode!=WhimTexGradientMode.Perceptual || gradient.Smoothness!=.6f || gradient.GetMidpoint(false,0)!=.2f), "JSON gradient settings lost.");
            context.True(!(new SDFLayerBehaviour().gradient.Mode!=WhimTexGradientMode.Perceptual), "SDF default must be Perceptual.");
            return;
        }
        finally
        {
            AsyncD.CleanupOwned(
                () => { if(editor!=null)editor.Close(); },
                () => host.Close());
        }
    }
}
