using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;
using WhimTex.Tests.UnityD;

public static class SwizzleUiTests
{
    public sealed class TestWindow : EditorWindow { }
    public static string Start(string runId) => AsyncD.Start(runId, Execute);
    public static string Poll(string runId) => AsyncD.Poll(runId);
    public static Task<string> Cancel(string runId) => AsyncD.Cancel(runId);
    public static Task<string> Cleanup(string runId) => AsyncD.Cleanup(runId);

    private static async Task Execute(TestContext context, CancellationToken token)
    {
        var document = ScriptableObject.CreateInstance<TextureCompositor>();
        var window = ScriptableObject.CreateInstance<TestWindow>();
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var assembly = typeof(TextureCompositor).Assembly;
        var ui = assembly.GetType("DCFApixels.WhimTex.WhimTexUI");
        var view = assembly.GetType("DCFApixels.WhimTex.LayerColorSettingsView");
        var bindingType = ui.GetNestedType("ValueBindings", BindingFlags.NonPublic);
        int changes = 0;
        object bindings = null;
        void Build()
        {
            window.rootVisualElement.Clear();
            var layer = document.layers[0];
            bindings = Activator.CreateInstance(bindingType, true);
            Action<string, Action> apply = (label, change) =>
            {
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(document, label);
                change();
                changes++;
                bindingType.GetMethod("Refresh").Invoke(bindings, new object[] { true });
            };
            view.GetMethod("Build", flags).Invoke(null, new object[]
                { window.rootVisualElement, layer, apply, bindings, true, null, document });
        }
        SwizzleChannel[] Channels() => Enumerable.Range(0, 4).Select(c => document.layers[0].swizzle[c]).ToArray();
        void CheckChannels(SwizzleChannel[] expected, string message)
        {
            for (int c = 0; c < 4; c++) context.Equal(expected[c], Channels()[c], message + " channel " + c);
        }
        try
        {
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 8;
            document.layers.Add(new ColorFillLayerBehaviour { color = new Color(.2f, .5f, .8f, .4f), opacity = .75f });
            window.titleContent = new GUIContent("Swizzle test");
            window.position = new Rect(100, 100, 480, 360);
            Build();
            window.Show();
            var root = window.rootVisualElement;
            for (int i = 0; i < 3; i++) await AsyncD.Tick(root, token);
            var presets = root.Q<ToolbarMenu>("swizzlePresets");
            context.True(presets != null, "Arrow preset button exists");
            var items = presets.menu.MenuItems().OfType<DropdownMenuAction>().ToArray();
            var names = new[] { "Default", "Default without Alpha", "R", "G", "B", "Luminance to Alpha", "Alpha to Grayscale" };
            context.Equal(names.Length, items.Length, "Builtin presets only, no duplicate A or import menu");
            for (int i = 0; i < names.Length; i++) context.Equal(names[i], items[i].name, "Preset label and order");
            foreach (string output in new[] { "R", "G", "B", "A" })
            {
                var field = root.Q<DropdownField>("swizzle" + output);
                context.True(field.choices.Contains("Luminance") && field.choices.Contains("Luminance * A"),
                    "Both luminance choices available for output " + output);
            }
            root.Q<DropdownField>("swizzleR").value = "Luminance * A";
            context.Equal(SwizzleChannel.LuminanceMultiplyA, document.layers[0].swizzle[0], "Dropdown applies the new channel");
            var expected = new[]
            {
                new[] { SwizzleChannel.R, SwizzleChannel.G, SwizzleChannel.B, SwizzleChannel.A },
                new[] { SwizzleChannel.R, SwizzleChannel.G, SwizzleChannel.B, SwizzleChannel.One },
                new[] { SwizzleChannel.Luminance, SwizzleChannel.Zero, SwizzleChannel.Zero, SwizzleChannel.One },
                new[] { SwizzleChannel.Zero, SwizzleChannel.Luminance, SwizzleChannel.Zero, SwizzleChannel.One },
                new[] { SwizzleChannel.Zero, SwizzleChannel.Zero, SwizzleChannel.Luminance, SwizzleChannel.One },
                new[] { SwizzleChannel.One, SwizzleChannel.One, SwizzleChannel.One, SwizzleChannel.Luminance },
                new[] { SwizzleChannel.A, SwizzleChannel.A, SwizzleChannel.A, SwizzleChannel.One }
            };
            for (int i = 0; i < names.Length; i++)
            {
                var before = Channels();
                int count = changes;
                items = root.Q<ToolbarMenu>("swizzlePresets").menu.MenuItems().OfType<DropdownMenuAction>().ToArray();
                items[i].Execute();
                context.Equal(count + 1, changes, "Preset uses one apply transaction");
                CheckChannels(expected[i], names[i]);
                context.Near(.75, document.layers[0].opacity, .00001, "Preset changes only Swizzle");
                var labels = expected[i].Select(c => c == SwizzleChannel.One ? "1" : c == SwizzleChannel.Zero ? "0" : c.ToString()).ToArray();
                int output = 0;
                foreach (string channel in new[] { "R", "G", "B", "A" })
                    context.Equal(labels[output++], root.Q<DropdownField>("swizzle" + channel).value, "Preset refreshes all selectors");
                Undo.PerformUndo();
                CheckChannels(before, "Undo restores the previous mapping");
                Undo.PerformRedo();
                CheckChannels(expected[i], "Redo restores the whole preset");
                Build();
                await AsyncD.Tick(root, token);
            }
            foreach (int width in new[] { 320, 480, 700 })
            {
                window.position = new Rect(100, 100, width, 360);
                await AsyncD.Tick(root, token);
                root.panel.Pick(Vector2.zero);
                presets = root.Q<ToolbarMenu>("swizzlePresets");
                context.Near(18, presets.worldBound.width, .2, "Preset button width");
                context.Near(presets.worldBound.width, presets.worldBound.height, .2, "Preset button is square");
                var channels = root.Query<DropdownField>(className: "whimtex-swizzle-channel").ToList();
                for (int i = 1; i < channels.Count; i++)
                    context.True(channels[i - 1].worldBound.xMax <= channels[i].worldBound.xMin + .1f, "Channel selectors do not overlap");
                context.True(channels[3].worldBound.xMax <= presets.worldBound.xMin + .1f, "Preset button stays right of all channels");
                context.True(presets.worldBound.xMax <= root.worldBound.xMax + .1f, "Preset button stays inside the inspector");
            }
        }
        finally
        {
            AsyncD.CleanupOwned(() => window.Close(), () => UnityEngine.Object.DestroyImmediate(window),
                () => Undo.ClearUndo(document), () => UnityEngine.Object.DestroyImmediate(document));
        }
    }
}
