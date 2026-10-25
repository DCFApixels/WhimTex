using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class ShaderFXPresetGroupTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type T(string name) => typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(string type, string method, params object[] args) => T(type).GetMethod(method, F).Invoke(null, args);
    static object Field(object value, string name) => value.GetType().GetField(name, F).GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, F).SetValue(value, data);
    static List<ShaderFXParameter> Parse(string source) => (List<ShaderFXParameter>)Call("ShaderFXMetadata", "Parse", source, false, null);
    static string Group(string source, out string error)
    {
        var args = new object[] { source, null };
        string group = (string)Call("ShaderFXMetadata", "ReadPresetGroup", args);
        error = (string)args[1];
        return group;
    }
    static object Entry(string name, string group = null, bool user = false, string error = null, string path = null)
    {
        var entry = Activator.CreateInstance(T("ShaderFXCatalog+Entry"), true);
        Set(entry, "menuPath", name); Set(entry, "group", group); Set(entry, "user", user);
        Set(entry, "path", path ?? name + ".hlsl"); Set(entry, "error", error);
        return entry;
    }
    static AdvancedDropdownItem Build(IList entries)
    {
        var root = new AdvancedDropdownItem("Test");
        Call("ShaderFXAddDropdown", "AddPresets", root, entries);
        return root;
    }
    static string Rows(AdvancedDropdownItem item) => string.Join("|", item.childList.Select(c => c.name == "SEPARATOR" ? "---" : c.name));
    static void Invalid(TestContext t, string source, string label)
    {
        try { Parse(source); t.True(false, label + " accepted invalid metadata"); }
        catch (TargetInvocationException e) { t.True(e.InnerException is FormatException, label + " has a format diagnostic"); }
    }

    public static string Run() => TestContext.Run("Preset groups: metadata, layout isolation, export and alphabetic menu blocks", t =>
    {
        const string body = "\nfloat4 ApplyFX(float4 color) { return color * _Amount; }\n";
        const string layout = "// @group(Settings)\n// @param float _Amount = 0.5\n// @endgroup\n";
        string source = "\uFEFF// @whimtex-effect Demo\n// @control(_Amount)\n// @group(\" Color \")\n" + layout + body;
        t.Equal("Color", Group(source, out string error), "Preset group trimmed");
        t.True(error == null, "Valid header has no error");
        var values = Parse(source);
        t.Equal(1, values.Count, "Metadata creates no parameter");
        t.Equal("Settings", values[0].controls[0].groupTitle, "Unquoted parameter group kept");
        t.Equal(1, values[0].controls[0].containers.Length, "Preset group is not a layout container");
        t.True(Group("/* // @group(\"Fake\") */\n" + layout + body, out error) == null && error == null, "Block comments and layout are not catalog groups");
        t.Equal("Shapes/Colors", Group("// @group(\"Shapes/Colors\")" + body, out error), "Slash in group name is literal");
        Invalid(t, "// @group(\"\")\n" + layout + body, "Empty group");
        Invalid(t, "// @group(\" \")\n" + layout + body, "Whitespace group");
        Invalid(t, "// @group(\"Color\"\n" + layout + body, "Malformed group");
        Invalid(t, "// @group(\"Color\")\n// @group(\"Tone\")\n" + layout + body, "Duplicate group");
        Invalid(t, layout + body + "// @group(\"Late\")", "Group after HLSL");
        Invalid(t, source + "// @group(\"Late\")", "Second group after HLSL");

        string exported = (string)Call("ShaderFXPresetWriter", "BuildParameterSource", source, values, "Exported", "whimtex-effect", null);
        t.Equal("Color", Group(exported, out error), "Preset export preserves grouping");
        t.True(error == null && exported.Replace("\r\n", "\n").StartsWith("// @whimtex-effect Exported\n// @control(_Amount)\n// @group(\"Color\")"), "Header order and main control preserved");
        t.Equal("Settings", Parse(exported)[0].controls[0].groupTitle, "Export also preserves parameter layout");
        foreach (string file in Directory.GetFiles("Packages/com.dcfapixels.whimtex/src/FXPresets", "*.hlsl"))
            t.True(Parse(File.ReadAllText(file)) != null, "Built-in parameter layout parses: " + Path.GetFileName(file));

        string temporary = Path.GetFullPath("Temp/WhimTex/fx-group-" + Guid.NewGuid().ToString("N") + ".hlsl");
        Directory.CreateDirectory(Path.GetDirectoryName(temporary));
        try
        {
            File.WriteAllText(temporary, source);
            var entry = Call("ShaderFXCatalog", "ReadHeader", temporary, temporary, true);
            t.Equal("Color", (string)Field(entry, "group"), "Header-only catalog gets group");
            t.True(Field(entry, "error") == null, "Catalog accepts grouped source");
            var restored = JsonUtility.FromJson(JsonUtility.ToJson(entry), entry.GetType());
            t.Equal("Color", (string)Field(restored, "group"), "Session snapshot retains group");
            var previous = JsonUtility.FromJson(JsonUtility.ToJson(entry), entry.GetType());
            t.True((bool)Call("ShaderFXCatalog", "SameEntry", previous, restored), "Unchanged session entry compares equal");
            Set(restored, "group", "Tone");
            t.True(!(bool)Call("ShaderFXCatalog", "SameEntry", previous, restored), "Group edits invalidate catalog entry");
            File.WriteAllText(temporary, "// @whimtex-effect Bad\n// @group(\"\")" + body);
            t.True(Field(Call("ShaderFXCatalog", "ReadHeader", temporary, temporary, true), "error") != null, "Bad metadata disables catalog entry with a reason");
        }
        finally { File.Delete(temporary); }

        var entries = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(T("ShaderFXCatalog+Entry")));
        entries.Add(Entry("Banana", "Tone")); entries.Add(Entry("Zebra", "Color"));
        entries.Add(Entry("Zulu")); entries.Add(Entry("Apricot", "Tone"));
        entries.Add(Entry("Aardvark", "color")); entries.Add(Entry("Alpha"));
        entries.Add(Entry("Nested/Zed", "Color")); entries.Add(Entry("Nested/Bee"));
        entries.Add(Entry("Nested/Alpha", "Color")); entries.Add(Entry("Nested/Beta", "Tone"));
        entries.Add(Entry("Loose", "Color", true));
        int effects = Resources.FindObjectsOfTypeAll<ShaderFX>().Length;
        int shaders = Resources.FindObjectsOfTypeAll<Shader>().Length;
        var root = Build(entries);
        t.Equal("Alpha|Nested|User|Zulu|---|Aardvark|Zebra|---|Apricot|Banana", Rows(root), "Alphabetic groups and labels, ungrouped block first");
        t.Equal("Bee|---|Alpha|Zed|---|Beta", Rows(root.childList.Single(i => i.name == "Nested")), "Groups are local to each submenu");
        t.Equal("Loose", Rows(root.childList.Single(i => i.name == "User")), "User library keeps groups without empty separator");
        t.True(root.childList.Single(i => i.name == "Aardvark").childList.Count == 0, "Group is not a submenu");
        var reversed = (IList)Activator.CreateInstance(entries.GetType());
        for (int i = entries.Count - 1; i >= 0; i--) reversed.Add(entries[i]);
        t.Equal(Rows(root), Rows(Build(reversed)), "Discovery order does not change menu order");
        entries.Clear(); entries.Add(Entry("Same", "Tone", path: "b.hlsl"));
        entries.Add(Entry("Same", "Color", error: "Broken", path: "a.hlsl"));
        root = Build(entries);
        t.Equal("Same (a.hlsl) — Broken|---|Same (b.hlsl)", Rows(root), "Duplicate names keep file identity across groups");
        t.True(!root.childList[0].enabled && root.childList[2].enabled, "Invalid preset stays disabled");
        entries.Clear(); entries.Add(Entry("Only", "Color"));
        t.Equal("Only", Rows(Build(entries)), "Single group has no leading or trailing separator");
        entries.Clear(); root = Build(entries);
        t.True(root.childList.Count == 1 && !root.childList[0].enabled && root.childList[0].name != "SEPARATOR", "Empty menu shows one disabled message");
        t.Equal(effects, Resources.FindObjectsOfTypeAll<ShaderFX>().Length, "Grouping creates no effects");
        t.Equal(shaders, Resources.FindObjectsOfTypeAll<Shader>().Length, "Grouping compiles no shaders");
    });
}
