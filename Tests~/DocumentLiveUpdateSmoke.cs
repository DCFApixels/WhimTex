// Live Update on the new document format: switch the carrier to an uncompressed surface, publish the
// composition, save during the session, and verify restoration and identity afterwards.
// Run with Unity Pipeline eval_file; the report is returned as the result string.
const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var report = new System.Text.StringBuilder();
int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new System.Exception("FAIL: " + message); checks++; }
object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
var sessionType = typeof(DCFApixels.WhimTex.TextureCompositor).Assembly.GetType("DCFApixels.WhimTex.WhimTexDocumentSession", true);
object CallStatic(string name, params object[] args)
{
    foreach (var method in sessionType.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        if (method.Name == name && method.GetParameters().Length == args.Length) return method.Invoke(null, args);
    throw new MissingMethodException(sessionType.FullName, name);
}
string StatusStatic() => (string)sessionType.GetProperty("Status", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null);
bool IsLive() => (bool)sessionType.GetProperty("IsLive", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null);

string dir = "Assets/WhimTexLive_" + Guid.NewGuid().ToString("N");
DCFApixels.WhimTex.TextureCompositor doc = null;
UnityEditor.AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(dir));
try
{
// After Reinitialize the CPU copy of the texture is undefined, so live pixels must be read back through the GPU.
UnityEngine.Color ReadGpu(UnityEngine.Texture2D texture, int x, int y)
{
    var rt = UnityEngine.RenderTexture.GetTemporary(texture.width, texture.height, 0, UnityEngine.RenderTextureFormat.ARGBFloat, UnityEngine.RenderTextureReadWrite.Linear);
    var cpu = new UnityEngine.Texture2D(texture.width, texture.height, UnityEngine.TextureFormat.RGBAFloat, false, true);
    var previous = UnityEngine.RenderTexture.active;
    bool srgb = UnityEngine.GL.sRGBWrite;
    try
    {
        UnityEngine.GL.sRGBWrite = false;
        UnityEngine.Graphics.Blit(texture, rt);
        UnityEngine.RenderTexture.active = rt;
        cpu.ReadPixels(new UnityEngine.Rect(0, 0, texture.width, texture.height), 0, 0);
        cpu.Apply(false, false);
        return cpu.GetPixel(x, y);
    }
    finally
    {
        UnityEngine.RenderTexture.active = previous;
        UnityEngine.GL.sRGBWrite = srgb;
        UnityEngine.RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(cpu);
    }
}

// --- document with a drawing layer and a fill layer ---
doc = UnityEngine.ScriptableObject.CreateInstance<DCFApixels.WhimTex.TextureCompositor>();
doc.width = doc.height = 16;
var drawing = new DCFApixels.WhimTex.DrawingLayerBehaviour { brushColor = UnityEngine.Color.red, brushSize = 16, brushHardness = 1f };
Call(drawing, "PaintPoint", new UnityEngine.Vector2(.5f, .5f), 16, 16, Call(drawing, "GetStrokeParameters", false));
Call(drawing, "SyncSurfaceToTexture");
doc.layers.Add(drawing);
var fill = new DCFApixels.WhimTex.Layer(new DCFApixels.WhimTex.ColorFillLayerBehaviour { color = UnityEngine.Color.blue });
doc.layers.Add(fill);
Call(doc, "NormalizeModel");
Call(doc, "MarkChanged");

string path = DCFApixels.WhimTex.WhimTexDocumentFile.Save(doc, dir + "/live-format-test");
report.Append("carrier=").Append(System.IO.Path.GetExtension(path));

// --- Live Update needs Read/Write on the carrier; the toggle itself must survive the session ---
var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
importer.isReadable = true;
importer.SaveAndReimport();
var target = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
Check(target != null && target.isReadable, "the carrier is readable after enabling Read/Write");
string metaBefore = System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path + ".meta"));
byte[] fileBefore = System.IO.File.ReadAllBytes(path);
var importedFormat = target.format;
string entity = target.GetEntityId().ToString();
report.Append(" importedFormat=").Append(importedFormat).Append(" mips=").Append(target.mipmapCount);

// --- start the session ---
Check((bool)CallStatic("Start", doc, path), "session starts: " + StatusStatic());
var live = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
Check(live.GetEntityId().ToString() == entity, "the same texture object is used");
Check(live.format != importedFormat, "the surface was switched to an uncompressed format, got " + live.format);
report.Append(" liveFormat=").Append(live.format);

// --- publish the composition: the surface must show the document, not the imported baseline ---
var baselineLength = System.IO.File.ReadAllBytes(path).Length;
CallStatic("Publish");
var published = ReadGpu(live, 1, 1);
Check(published.b > published.r && published.b > published.g, "the live surface shows the composition, got " + published.ToString("F2"));
Check(live.GetEntityId().ToString() == entity, "identity survives publishing");
Check(System.IO.File.ReadAllBytes(path).Length == baselineLength, "publishing does not touch the file");
report.Append(" published=").Append(published.ToString("F2"));

// --- a document change must reach the live surface ---
var fillBehaviour = (DCFApixels.WhimTex.ColorFillLayerBehaviour)fill.Behaviour;
fillBehaviour.color = UnityEngine.Color.green;
Call(doc, "MarkChanged");
CallStatic("Publish");
var liveGreen = ReadGpu(live, 1, 1);
Check(liveGreen.g > liveGreen.r && liveGreen.g > liveGreen.b, "a document change reaches the live surface, got " + liveGreen.ToString("F2"));

// --- save during the session ---
Check((bool)CallStatic("Save", doc, path), "save during a live session succeeds: " + StatusStatic());
var afterSave = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
Check(afterSave != null && afterSave.GetEntityId().ToString() == entity, "identity survives a save during the session");
Check(IsLive(), "the session restarted after saving");
Check(afterSave.format != importedFormat, "the restarted session is live again");
var savedPixel = ReadGpu(afterSave, 1, 1);
Check(savedPixel.g > .5f, "the saved image holds the new composition, got " + savedPixel.ToString("F2"));
report.Append(" saved=").Append(savedPixel.ToString("F2"));

// --- stop and verify restoration ---
CallStatic("Stop", "test");
var restored = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
Check(restored.format == importedFormat, "the imported format is restored, got " + restored.format);
Check(restored.GetPixel(1, 1).g > .5f, "the restored image keeps the saved pixels, got " + restored.GetPixel(1, 1).ToString("F2"));
Check(!IsLive(), "the session is closed");
string metaAfter = System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path + ".meta"));
Check(metaBefore == metaAfter, "the .meta was not modified by the session");
Check(!UnityEditor.EditorUtility.IsDirty(restored), "the texture is not left dirty");
report.Append(" restoredFormat=").Append(restored.format);

// --- a plain image cannot be used as a live document ---
Check(!(bool)CallStatic("Start", doc, dir + "/missing-document.png"), "a missing carrier is reported instead of throwing");
return "PASS: live update checks=" + checks + ", " + report;
}
finally
{
    try { if (IsLive()) CallStatic("Stop", "historical smoke cleanup"); } catch { }
    if (doc != null && !UnityEditor.AssetDatabase.Contains(doc)) UnityEngine.Object.DestroyImmediate(doc);
    UnityEditor.AssetDatabase.DeleteAsset(dir);
}
