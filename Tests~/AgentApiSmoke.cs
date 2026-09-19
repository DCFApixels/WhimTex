var checks = 0;
var fixture = "Assets/WhimTexApiSmoke_" + System.Guid.NewGuid().ToString("N");
var projectRoot = System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName;
var temp = System.IO.Path.Combine(projectRoot, "Temp/WhimTex");
System.IO.Directory.CreateDirectory(temp);
var sourcePng = System.IO.Path.Combine(temp, System.Guid.NewGuid().ToString("N") + ".png");
var image = new UnityEngine.Texture2D(32, 16, UnityEngine.TextureFormat.RGBA32, false);
try
{
    var colors = new UnityEngine.Color32[32 * 16];
    for (int i = 0; i < colors.Length; i++) colors[i] = new UnityEngine.Color32(0, 0, 255, 255);
    image.SetPixels32(colors);
    image.Apply();
    System.IO.File.WriteAllBytes(sourcePng, UnityEngine.ImageConversion.EncodeToPNG(image));
}
finally { UnityEngine.Object.DestroyImmediate(image); }

var previews = new System.Collections.Generic.List<string>();
try
{
// Resolve the API's JSON assembly explicitly: some Editor packages embed another copy.
var jsonType = typeof(DCFApixels.WhimTex.WhimTexApi)
    .GetMethod("SetBrush", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
    .GetParameters().Single(p => p.ParameterType.FullName == "Newtonsoft.Json.Linq.JObject").ParameterType;
object Json(string value) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { value });
object At(object value, params object[] keys)
{
    foreach (var key in keys)
        value = value.GetType().GetProperty("Item", new[] { key.GetType() }).GetValue(value, new[] { key });
    return value;
}
void Set(object value, object key, object item)
{
    var tokenType = jsonType.Assembly.GetType("Newtonsoft.Json.Linq.JToken");
    var token = tokenType.IsInstanceOfType(item) ? item :
        tokenType.GetMethod("FromObject", new[] { typeof(object) }).Invoke(null, new[] { item });
    value.GetType().GetProperty("Item", new[] { key.GetType() }).SetValue(value, token, new[] { key });
}
string Text(object value, params object[] keys) => At(value, keys).ToString();
bool Flag(object value, params object[] keys) => bool.Parse(Text(value, keys));
float Number(object value, params object[] keys) => float.Parse(Text(value, keys), System.Globalization.CultureInfo.InvariantCulture);
void Check(bool condition, string message)
{
    if (!condition) throw new System.Exception(message + " | Fixtures: " + fixture);
    checks++;
}
object Ok(string response)
{
    var result = Json(response);
    Check(Flag(result, "success"), result.ToString());
    return result;
}
object Inspect() => Ok(DCFApixels.WhimTex.WhimTexApi.Inspect(fixture + "/Icon.asset"));
object Batch(string operations)
{
    var request = Json("{\"apiVersion\":1,\"save\":false,\"operations\":" + operations + "}");
    Set(request, "assetPath", fixture + "/Icon.asset");
    Set(request, "expectedRevision", At(Inspect(), "document", "revision"));
    return request;
}
void Reject(object request, string code)
{
    var result = Json(DCFApixels.WhimTex.WhimTexApi.ExecuteJson(request.ToString()));
    Check(!Flag(result, "success") && Text(result, "errorCode") == code, "Expected " + code + ": " + result);
}
string LayerId(object document, string name)
{
    foreach (var layer in (System.Collections.IEnumerable)At(document, "layers"))
        if (Text(layer, "settings", "name") == name) return Text(layer, "id");
    throw new System.Exception("Missing layer: " + name);
}
UnityEngine.Color Pixel(string suffix, int x, int y)
{
    var result = Ok(DCFApixels.WhimTex.WhimTexApi.Render(fixture + "/Icon.asset",
        "Temp/WhimTex/" + System.Guid.NewGuid().ToString("N") + "-" + suffix + ".png", 64));
    var texture = new UnityEngine.Texture2D(2, 2);
    previews.Add(Text(result, "outputPath"));
    try
    {
        Check(UnityEngine.ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(Text(result, "outputPath"))), "Preview decodes");
        return texture.GetPixel(x, y);
    }
    finally { UnityEngine.Object.DestroyImmediate(texture); }
}

Ok(DCFApixels.WhimTex.WhimTexApi.ImportImage(sourcePng, fixture + "/source.png"));
var create = Json(@"{
  'apiVersion':1, 'create':true, 'width':64, 'height':64,
  'operations':[
    {'op':'add','type':'group','as':'art','settings':{'name':'Art'}},
    {'op':'add','type':'file','as':'image','parent':'@art','settings':{'name':'Image'}},
    {'op':'add','type':'drawing','as':'ink','settings':{'name':'Ink'}},
    {'op':'stroke','layer':'@ink','brush':{'size':5,'hardness':1,'color':[1,0,0,1]},'points':[[8,8],[20,8]]},
    {'op':'add','type':'outline','as':'edge','settings':{'name':'Edge','enabled':false}},
    {'op':'target','layer':'@edge','target':'@art'}
  ]
}".Replace('\'', '"'));
Set(create, "assetPath", fixture + "/Icon.asset");
Set(At(create, "operations", 1, "settings"), "source", fixture + "/source.png");
Set(create, "dryRun", true);
Ok(DCFApixels.WhimTex.WhimTexApi.ExecuteJson(create.ToString()));
Check(!System.IO.File.Exists(System.IO.Path.Combine(projectRoot, fixture, "Icon.asset")), "Dry run does not create a document");
Set(create, "dryRun", false);
var created = Ok(DCFApixels.WhimTex.WhimTexApi.ExecuteJson(create.ToString()));
var doc = At(created, "document");
Check(Flag(doc, "hasOutputTexture") && Flag(doc, "hasOutputSprite"), "Output texture and sprite exist");
Check(((System.Collections.ICollection)At(doc, "layers")).Count == 4, "Four layers including a group child");
var inkId = LayerId(doc, "Ink");
var imageId = LayerId(doc, "Image");
var artId = LayerId(doc, "Art");
foreach (var layer in (System.Collections.IEnumerable)At(doc, "layers"))
    if (Text(layer, "id") == imageId)
        Check(System.Math.Abs(Number(layer, "transform", "scale", 1) - 0.5f) < 0.0001f, "Initial File assignment fits source aspect");
var pixel = Pixel("initial", 12, 56);
Check(pixel.r > 0.9f && pixel.a > 0.9f, "Top-left canvas stroke paints expected pixel");
Reject(create, "already_exists");

var invalid = Batch("[{\"op\":\"set\",\"layer\":\"" + inkId + "\",\"settings\":{\"opacity\":0.2}}, {\"op\":\"set\",\"layer\":\"missing\",\"settings\":{\"opacity\":0.1}}]");
var beforeInvalid = Text(Inspect(), "document", "revision");
Reject(invalid, "layer_not_found");
Check(Text(Inspect(), "document", "revision") == beforeInvalid, "Failed preflight leaves model unchanged");
var typo = Batch("[{\"op\":\"set\",\"layer\":\"" + inkId + "\",\"settings\":{\"opactiy\":0.2}}]");
Reject(typo, "invalid_request");
var duplicateAlias = Batch("[{\"op\":\"add\",\"type\":\"color\",\"as\":\"same\"},{\"op\":\"add\",\"type\":\"color\",\"as\":\"same\"}]");
Reject(duplicateAlias, "invalid_request");
var cyclic = Batch("[{\"op\":\"move\",\"layer\":\"" + artId + "\",\"parent\":\"" + artId + "\"}]");
Reject(cyclic, "invalid_request");
var effectCycle = Batch("[{\"op\":\"add\",\"type\":\"sdf\",\"as\":\"a\"},{\"op\":\"add\",\"type\":\"sdf\",\"as\":\"b\"},{\"op\":\"target\",\"layer\":\"@a\",\"target\":\"@b\"},{\"op\":\"target\",\"layer\":\"@b\",\"target\":\"@a\"}]");
Reject(effectCycle, "invalid_target");

var edit = Batch("[{\"op\":\"set\",\"layer\":\"" + inkId + "\",\"settings\":{\"opacity\":0.5}}]");
Ok(DCFApixels.WhimTex.WhimTexApi.ExecuteJson(edit.ToString()));
Reject(edit, "revision_conflict");
UnityEditor.Undo.PerformUndo();
Check(Pixel("undo-opacity", 12, 56).a > 0.9f, "Undo restores opacity");
UnityEditor.Undo.PerformRedo();
Check(System.Math.Abs(Pixel("redo-opacity", 12, 56).a - 0.5f) < 0.03f, "Redo restores opacity edit");

var paint = Batch("[{\"op\":\"stroke\",\"layer\":\"" + inkId + "\",\"brush\":{\"color\":[0,1,0,1],\"size\":5,\"hardness\":1},\"points\":[[40,8],[52,8]]}]");
Ok(DCFApixels.WhimTex.WhimTexApi.ExecuteJson(paint.ToString()));
Check(Pixel("paint", 44, 56).g > 0.9f, "API brush paints a second stroke");
UnityEditor.Undo.PerformUndo();
Check(Pixel("undo-paint", 44, 56).a < 0.01f, "Undo restores pixels without a WhimTex window");
UnityEditor.Undo.PerformRedo();
Check(Pixel("redo-paint", 44, 56).g > 0.9f, "Redo restores drawing pixels");

var transform = Batch("[{\"op\":\"transform\",\"layer\":\"" + imageId + "\",\"transform\":{\"position\":[10,-4],\"rotation\":30}}]");
Ok(DCFApixels.WhimTex.WhimTexApi.ExecuteJson(transform.ToString()));
var save = Batch("[]");
Set(save, "save", true);
Ok(DCFApixels.WhimTex.WhimTexApi.ExecuteJson(save.ToString()));
var reloaded = Inspect();
Check(Flag(reloaded, "document", "hasOutputTexture") && Flag(reloaded, "document", "hasOutputSprite"), "Rebaked subassets remain available");
return new { success = true, checks };
}
catch (System.Exception error) { return new { success = false, checks, error = error.ToString() }; }
finally
{
    UnityEditor.AssetDatabase.DeleteAsset(fixture);
    System.IO.File.Delete(sourcePng);
    foreach (var preview in previews) System.IO.File.Delete(preview);
}
