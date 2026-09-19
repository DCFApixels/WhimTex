// Temporary cleanup: removes the generated manual serialization and the partial modifier it added.
// A type that has several declarations (a class split across files) keeps its partial modifier.
// Run with Unity Pipeline eval_file.
string root = "Packages/com.dcfapixels.whimtex/src";
string generated = root + "/WhimTexDocumentSerialization.Generated.cs";
if (!System.IO.File.Exists(generated)) return "SKIP: the generated file is not there";

string[] files = System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories);
string text = System.IO.File.ReadAllText(generated);
var names = new System.Collections.Generic.List<string>();
foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"class (\w+)"))
    if (!names.Contains(match.Groups[1].Value)) names.Add(match.Groups[1].Value);

int unpatched = 0;
var report = new System.Text.StringBuilder();
foreach (string name in names)
{
    var declarations = new System.Collections.Generic.List<string>();
    var pattern = new System.Text.RegularExpressions.Regex(@"\bclass " + name + @"\b");
    foreach (string file in files)
    {
        if (file.EndsWith("Generated.cs", StringComparison.Ordinal)) continue;
        if (!pattern.IsMatch(System.IO.File.ReadAllText(file))) continue;
        declarations.Add(file);
    }
    if (declarations.Count != 1)
    {
        report.Append(name).Append("(").Append(declarations.Count).Append(" declarations) ");
        continue;
    }
    string source = System.IO.File.ReadAllText(declarations[0]);
    System.IO.File.WriteAllText(declarations[0],
        System.Text.RegularExpressions.Regex.Replace(source, @"\bpartial class " + name + @"\b", "class " + name));
    unpatched++;
}

System.IO.File.Delete(generated);
if (System.IO.File.Exists(generated + ".meta")) System.IO.File.Delete(generated + ".meta");
return "REMOVED: types=" + names.Count + " unpatched=" + unpatched + " leftSplit=[" + report + "]";
