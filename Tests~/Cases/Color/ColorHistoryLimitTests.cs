using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests;

public static class ColorHistoryLimitTests
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Preference = "DCFApixels.WhimTex.ColorHistory.MaxColors";
    static readonly Type Settings = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexUserSettings", true);
    static readonly PropertyInfo Limit = Settings.GetProperty("ColorHistoryLimit", Flags);
    static List<Color> History(WhimTexDocument document) => (List<Color>)typeof(WhimTexDocument).GetField("colorHistory", Flags).GetValue(document);
    static void Remember(WhimTexDocument document, Color color) => typeof(WhimTexDocument).GetMethod("RememberColor", Flags).Invoke(document, new object[] { color });
    static void SetLimit(int value) => Limit.SetValue(null, value);

    public static string Run() => TestContext.Run("Bounded Color History", test =>
    {
        bool existed = EditorPrefs.HasKey(Preference);
        int previous = EditorPrefs.GetInt(Preference);
        var owned = new List<WhimTexDocument>();
        WhimTexDocument Create()
        {
            var document = ScriptableObject.CreateInstance<WhimTexDocument>();
            owned.Add(document);
            return document;
        }
        void EqualHistory(WhimTexDocument document, IEnumerable<Color> expected, string message)
            => test.True(History(document).SequenceEqual(expected), message);
        Undo.IncrementCurrentGroup();
        try
        {
            EditorPrefs.DeleteKey(Preference);
            test.Equal(256, (int)Limit.GetValue(null), "Default limit");
            var document = Create();
            var colors = Enumerable.Range(0, 260).Select(i => new Color(i + .25f, -.5f, 2, .375f)).ToArray();
            foreach (Color color in colors) Remember(document, color);
            test.Equal(256, History(document).Count, "History remains bounded after 260 additions");
            EqualHistory(document, colors.Skip(4).Reverse(), "Only oldest entries evicted; HDR and alpha exact");
            Color reused = History(document)[200];
            var expected = History(document).Where(color => !color.Equals(reused)).Prepend(reused).ToArray();
            Remember(document, reused);
            EqualHistory(document, expected, "Reuse promotes exact RGBA without losing another color");
            SetLimit(3);
            test.Equal(3, EditorPrefs.GetInt(Preference), "User limit persisted");
            test.Equal(256, History(document).Count, "Changing preference alone does not delete document colors");
            Remember(document, reused);
            EqualHistory(document, expected.Take(3), "Reusing first color also applies a reduced limit");
            Remember(document, new Color(float.NaN, 0, 0));
            EqualHistory(document, expected.Take(3), "Invalid color does not evict history");
            SetLimit(0);
            test.Equal(1, (int)Limit.GetValue(null), "Minimum limit is one");
            var transparentHdr = new Color(16, -2, .5f, 0);
            Remember(document, transparentHdr);
            EqualHistory(document, new[] { transparentHdr }, "Limit one retains exact hidden HDR RGB");

            SetLimit(3);
            var undoDocument = Create();
            var before = new[] { Color.red, Color.green, Color.blue };
            History(undoDocument).AddRange(before);
            Undo.FlushUndoRecordObjects();
            foreach (var item in owned) Undo.ClearUndo(item);
            Undo.IncrementCurrentGroup();
            Remember(undoDocument, transparentHdr);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            EqualHistory(undoDocument, new[] { transparentHdr, Color.red, Color.green }, "Add and eviction share one operation");
            Undo.PerformUndo();
            EqualHistory(undoDocument, before, "Undo restores evicted color and order");
            Undo.PerformRedo();
            EqualHistory(undoDocument, new[] { transparentHdr, Color.red, Color.green }, "Redo restores bounded history");
            EqualHistory(document, new[] { transparentHdr }, "Other document history stays independent");

            var serializer = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSerializer", true);
            using (var container = new WhimTexDocumentContainer())
            {
                byte[] bytes = (byte[])serializer.GetMethod("Serialize", Flags).Invoke(null, new object[] { undoDocument, container });
                test.True(Encoding.UTF8.GetString(bytes).Contains("colorHistory"), "Native model stores canonical field name");
                var result = serializer.GetMethod("Deserialize", Flags).Invoke(null, new object[] { bytes, container, typeof(WhimTexDocument), null, false });
                var copy = (WhimTexDocument)result.GetType().GetProperty("Model", Flags).GetValue(result);
                owned.Add(copy);
                EqualHistory(copy, History(undoDocument), "Native model roundtrip keeps order and exact RGBA");
            }
            string json = WhimTexDocumentJson.Write(undoDocument, new WhimTexJsonWriteOptions { Mode = WhimTexJsonWriteMode.Full }).Json;
            test.True(json.Contains("\"colorHistory\"") && !json.Contains("\"documentColors\""), "JSON uses Color History without alias");
            SetLimit(1);
            using (var result = WhimTexDocumentJson.Read(json, false))
                EqualHistory(result.Document, History(undoDocument), "Loading keeps saved history until next addition");
            bool rejected = false;
            try { using var result = WhimTexDocumentJson.Read(json.Replace("\"colorHistory\"", "\"documentColors\""), false); }
            catch (WhimTexDocumentException) { rejected = true; }
            test.True(rejected, "Unsupported field is rejected instead of migrated");
        }
        finally
        {
            Undo.FlushUndoRecordObjects();
            foreach (var document in owned)
                if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            Undo.IncrementCurrentGroup();
            if (existed) EditorPrefs.SetInt(Preference, previous); else EditorPrefs.DeleteKey(Preference);
        }
    });
}
