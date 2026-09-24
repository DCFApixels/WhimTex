using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using DCFApixels.WhimTex;

public static class UIRefreshSmoke
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static string Main()
    {
        var effect = ScriptableObject.CreateInstance<ShaderFX>();
        try
        {
            var parameters = (List<ShaderFXParameter>)typeof(ShaderFX).GetField("parameters", Hidden).GetValue(effect);
            parameters.Clear();
            for (int i = 0; i < 32; i++)
                parameters.Add(new ShaderFXParameter { name = "_Value" + i, floatValue = i });
            var condition = new ShaderFXParameterControl { type = ShaderFXParameterType.Float,
                visibleIfParameter = "_Value0", visibleIfValue = 1, headers = new[] { "Conditional" } };
            parameters[1].controls.Add(condition);
            var mode = new ShaderFXParameterControl { type = ShaderFXParameterType.Enum,
                optionNames = new[] { "First", "Second" }, optionValues = new[] { 0f, 1f } };
            parameters[2].controls.Add(mode);
            var type = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderFXParameterView", true);
            var view = (VisualElement)Activator.CreateInstance(type, Hidden, null, new object[] { effect }, null);
            var refresh = (Action)Delegate.CreateDelegate(typeof(Action), view, type.GetMethod("Refresh", Hidden));
            var first = view[0];
            var conditional = view.Q(className: "whimtex-fx-conditional-parameter");
            Check(conditional.style.display.value == DisplayStyle.None, "Condition should start hidden");
            parameters[0].floatValue = 1;
            refresh();
            Check(ReferenceEquals(first, view[0]), "Value update rebuilt controls");
            Check(conditional.style.display.value == DisplayStyle.Flex, "Condition failed to show");
            parameters[0] = new ShaderFXParameter { id = parameters[0].id, name = "_Value0", floatValue = 0 };
            refresh();
            Check(ReferenceEquals(first, view[0]), "Model replacement rebuilt an unchanged layout");
            Check(conditional.style.display.value == DisplayStyle.None, "Condition uses stale model object");
            Check(((FloatField)first).value == 0, "Field uses stale model object");

            Action<Action, string> expectRebuild = (edit, description) => {
                var before = view[0]; edit(); refresh();
                Check(!ReferenceEquals(before, view[0]), "Missing rebuild: " + description);
            };
            expectRebuild(() => condition.headers[0] = "Updated header", "header array edit");
            expectRebuild(() => condition.visibleIfNotEqual = true, "visibility condition edit");
            expectRebuild(() => mode.optionNames[1] = "Changed", "enum label edit");
            expectRebuild(() => mode.optionValues[1] = 2, "enum value edit");
            expectRebuild(() => condition.tooltip = "Updated tooltip", "tooltip edit");
            expectRebuild(() => parameters[3].maximum = 10, "range edit");
            expectRebuild(() => parameters.Reverse(), "reorder");
            expectRebuild(() => parameters.RemoveAt(0), "remove");
            expectRebuild(() => parameters.Add(new ShaderFXParameter { name = "_Added" }), "add");
            first = view[0];
            for (int i = 0; i < 20; i++) refresh();
            Check(ReferenceEquals(first, view[0]), "Idle refresh rebuilt controls");

            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) refresh();
            long currentBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            start = GC.GetAllocatedBytesForCurrentThread();
            string key = null;
            for (int i = 0; i < 200; i++) key = LegacyLayoutKey(parameters);
            long legacyKeyBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            GC.KeepAlive(key);
            if (legacyKeyBytes > 0) Check(currentBytes < legacyKeyBytes, "Refresh allocation regression");
            return "PASS: stable controls, conditional visibility, model replacement, headers/enums/ranges, reorder/add/remove. " +
                (legacyKeyBytes == 0 ? "Allocation counter unavailable in this Editor; no allocation claim." :
                "200 refreshes, 32 parameters: current full refresh allocated " + currentBytes +
                " bytes; former layout-key construction alone allocated " + legacyKeyBytes + " bytes.");
        }
        finally { UnityEngine.Object.DestroyImmediate(effect); }
    }

    static string LegacyLayoutKey(List<ShaderFXParameter> parameters)
    {
        string key = "";
        foreach (var p in parameters)
            if (p != null)
            {
                key += $"{p.id}:{p.name}:{p.type}:{p.hasMinimum}:{p.minimum}:{p.hasMaximum}:{p.maximum}:{p.softMinimum}:{p.softMaximum}|";
                foreach (var control in p.controls) key += JsonUtility.ToJson(control);
            }
        return key;
    }

    public static async Task<string> Bindings()
    {
        var previousWindow = EditorWindow.focusedWindow;
        var window = ScriptableObject.CreateInstance<EditorWindow>();
        window.titleContent = new GUIContent("UI bindings smoke");
        try
        {
            window.ShowUtility();
            var field = new IntegerField();
            window.rootVisualElement.Add(field);
            var ui = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.WhimTexUI", true);
            var type = ui.GetNestedType("ValueBindings", BindingFlags.NonPublic);
            var bindings = Activator.CreateInstance(type, true);
            int model = 1, reads = 0;
            type.GetMethod("Track").MakeGenericMethod(typeof(int)).Invoke(bindings,
                new object[] { field, (Func<int>)(() => { reads++; return model; }) });
            Action queue = () => {
                using (var evt = PointerUpEvent.GetPooled()) { evt.target = field; field.SendEvent(evt); }
            };
            Func<Task> tick = () => {
                var completion = new TaskCompletionSource<bool>();
                window.rootVisualElement.schedule.Execute(() => completion.SetResult(true)).StartingIn(50);
                return completion.Task;
            };
            model = 2; reads = 0;
            queue(); queue(); queue();
            await tick();
            Check(field.value == 2 && reads == 1, "Refreshes did not coalesce");
            model = 3; reads = 0;
            queue(); await tick();
            Check(field.value == 3 && reads == 1, "Completed task did not restart");
            model = 4; reads = 0;
            queue(); field.RemoveFromHierarchy();
            await tick();
            Check(reads == 0, "Detached field still refreshed");
            window.rootVisualElement.Add(field);
            queue(); await tick();
            Check(field.value == 4 && reads == 1, "Reattached field did not refresh");
            return "PASS: binding event coalescing, scheduled-item reuse, detach cancellation and reattach.";
        }
        finally
        {
            window.Close();
            if (previousWindow != null) previousWindow.Focus();
        }
    }
}
