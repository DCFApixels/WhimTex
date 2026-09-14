// Run via Pipeline after manual compilation. Parser-only, no asset writes or Undo.
var type = typeof(DCFApixels.WhimTex.Layer).Assembly.GetType("DCFApixels.WhimTex.MissingLayerData");
if (type.GetMethod("TypedBlock", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic) == null)
    throw new Exception("The Editor still has the old parser loaded. Compile manually before running this check.");
var method = type.GetMethod("Parse", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
// Reflection avoids the Localization editor's private Newtonsoft fork in eval's references.
object Parse(string text) => method.Invoke(null, new object[] { text });
object At(object token, params object[] path)
{
    foreach (object key in path) token = token.GetType().GetProperty("Item", new[] { key.GetType() }).GetValue(token, new[] { key });
    return token;
}
object Value(object token, params object[] path) { var leaf = At(token, path); return leaf.GetType().GetProperty("Value").GetValue(leaf); }
float Number(object token, params object[] path) => Convert.ToSingle(Value(token, path), System.Globalization.CultureInfo.InvariantCulture);
string Text(object token, params object[] path) => (string)Value(token, path);
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
// Captured native Unity missing-type payload (IDs shortened for this fixture).
var native = Parse("recoveryId \"44acd5d2403144928465014175024565\" (string)\nstrength 2.75 (float)\nseed 613 (int)\n");
Check(Text(native,"recoveryId") == "44acd5d2403144928465014175024565", "Native stable recovery ID");
Check(Number(native,"strength") == 2.75f && Number(native,"seed") == 613, "Native numerical fields");
// Representative diagnostic-tree forms: these do not replace the real missing-type round-trip test.
var nested = Parse("size 4 (int)\ncenter (Vector2f)\n\tx 0.2 (float)\n\ty 0.7 (float)\nmessage \"a (string): \\\"quoted\\\"\\nnext\" (string)\nsamples (vector)\n\tArray (Array)\n\t\tsize 1 (int)\n\t\tdata (Vector2f)\n\t\t\tx 0.3 (float)\n\t\t\ty 0.6 (float)\n");
Check(Number(nested,"size") == 4, "A size field is not mistaken for an array");
Check(Number(nested,"center","y") == .7f, "Nested vector");
Check(Text(nested,"message") == "a (string): \"quoted\"\nnext", "Quoted text is data, not field syntax");
Check(At(nested,"samples").GetType().Name == "JArray" && Number(nested,"samples",0,"x") == .3f, "Nested list");
Check(Text(Parse("recoveryId \"0012\" (string)"),"recoveryId") == "0012", "Numeric strings retain leading zeros");
Check(Number(Parse("seed: 613\n"),"seed") == 613, "YAML form retained");
Check(Number(Parse("{\"seed\":613}"),"seed") == 613, "JSON form retained");
foreach (string invalid in new[] { "seed 1 (int)\nseed 2 (int)", "samples (vector)\n  Array (Array)\n    size 2 (int)\n    data 1 (float)", "samples (vector)\n  Array (Array)\n    size 999999999 (int)" })
{
    bool rejected = false;
    try { Parse(invalid); } catch (System.Reflection.TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
    Check(rejected, "Malformed/oversized data is rejected");
}
return new { success = true, checks };
