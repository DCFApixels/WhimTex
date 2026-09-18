using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;

public static class SpriteEditorCompileSmoke
{
    public static Task<string> Main()
    {
        string templates = Path.Combine(EditorApplication.applicationContentsPath, "Resources/PackageManager/ProjectTemplates/libcache");
        string dependency = Directory.GetFiles(templates, "Unity.2D.Sprite.Editor.dll", SearchOption.AllDirectories).First();
        string directory = Path.Combine("Temp/WhimTex", "SpriteEditorCompile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var completion = new TaskCompletionSource<string>();
        var builder = new AssemblyBuilder(Path.Combine(directory, "DCFApixels.WhimTex.SpriteEditor.dll"),
            new[] { "Packages/com.dcfapixels.whimtex/src/Integrations/SpriteEditor/WhimTexSpriteDataProvider.cs" })
        {
            flags = AssemblyBuilderFlags.EditorAssembly,
            additionalReferences = new[] { dependency, typeof(DCFApixels.WhimTex.TextureCompositor).Assembly.Location,
                typeof(Editor).Assembly.Location, typeof(UnityEngine.Object).Assembly.Location }
        };
        builder.excludeReferences = builder.defaultReferences.Where(p => Path.GetFileName(p) == "UnityEngine.dll" || Path.GetFileName(p) == "UnityEditor.dll").ToArray();
        builder.buildFinished += (path, messages) =>
        {
            string errors = string.Join("\n", messages.Where(m => m.type == CompilerMessageType.Error).Select(m => m.message));
            if (errors.Length == 0) completion.SetResult("PASS: optional adapter compiled against the installed Editor's template Sprite Editor assembly (no package installed).");
            else completion.SetException(new Exception(errors));
        };
        if (!builder.Build()) throw new Exception("Unity refused the validation compilation.");
        return completion.Task;
    }
}
