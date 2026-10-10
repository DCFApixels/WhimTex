using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    internal sealed class BrushParameterTextureReference
    {
        public string parameterId, guid;
        public long localId;

        internal static List<BrushParameterTextureReference> Capture(IReadOnlyList<ShaderFXParameter> parameters)
        {
            var references = new List<BrushParameterTextureReference>();
            foreach (var p in parameters)
            {
                if (p.type != ShaderFXParameterType.Texture2D || p.textureValue == null) continue;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.textureValue, out string guid, out long localId) || string.IsNullOrEmpty(guid))
                    throw new FormatException("Brush texture parameter " + p.name + " must reference a project texture asset.");
                references.Add(new BrushParameterTextureReference { parameterId = p.id, guid = guid, localId = localId });
            }
            return references;
        }

        internal static void Restore(IReadOnlyList<ShaderFXParameter> parameters, IReadOnlyList<BrushParameterTextureReference> references)
        {
            if (references == null) return;
            foreach (var reference in references)
            {
                ShaderFXParameter parameter = null;
                foreach (var p in parameters) if (p.id == reference.parameterId && p.type == ShaderFXParameterType.Texture2D) { parameter = p; break; }
                if (parameter == null) continue;
                string path = AssetDatabase.GUIDToAssetPath(reference.guid);
                Texture2D found = null;
                if (!string.IsNullOrEmpty(path))
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (asset is Texture2D texture && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string _, out long id) && id == reference.localId)
                        { found = texture; break; }
                if (found == null) throw new FormatException("Missing texture asset for brush parameter " + parameter.name + ": " + reference.guid);
                parameter.textureValue = found;
            }
        }
    }

    internal sealed class BrushTipProgram : IDisposable
    {
        internal const string DefaultSource = "// @whimtex-brush Shapes/Ring\n// @param float _Thickness = 0.15 [0.01 .. 0.5]\n\nfloat4 BrushTip(float2 uv)\n{\n    float d = abs(length(uv * 2.0 - 1.0) - 0.65);\n    float aa = max(fwidth(d), 0.001);\n    return float4(1, 1, 1, 1 - smoothstep(_Thickness, _Thickness + aa, d));\n}\n";
        internal static string Folder => Path.Combine(BrushPresetLibrary.Folder, "HLSL");
        private Shader shader;
        private Material material;
        private string compiledSource;
        private List<ShaderFXParameter> compiledParameters;
        private ShaderParameterBindings parameterBindings;
        internal string Diagnostics { get; private set; }
        internal bool IsDynamic { get; private set; }
        internal Material DynamicMaterial => IsDynamic ? material : null;
        internal bool Matches(string source) => material != null && compiledSource == source;

        internal static bool ReadDynamic(string source)
        {
            var masked = new StringBuilder();
            bool block = false;
            using var lines = new StringReader(source ?? "");
            string line;
            while ((line = lines.ReadLine()) != null)
                masked.AppendLine(ShaderFXSourceBuilder.MaskComments(line, ref block));
            string text = masked.ToString();
            int definitions = 0, depth = 0, cursor = 0;
            bool dynamic = false;
            foreach (Match match in Regex.Matches(text, @"\bfloat4\s+BrushTip\s*\(([^()]*)\)\s*\{"))
            {
                for (; cursor < match.Index; cursor++)
                    if (text[cursor] == '{') depth++; else if (text[cursor] == '}') depth--;
                if (depth != 0) continue;
                string args = match.Groups[1].Value;
                bool stat = Regex.IsMatch(args, @"^\s*(?:in\s+)?float2\s+\w+\s*$");
                bool dyn = Regex.IsMatch(args, @"^\s*(?:in\s+)?float2\s+\w+\s*,\s*(?:in\s+)?DynamicBrushContext\s+\w+\s*$");
                if (!stat && !dyn) throw new FormatException("Use float4 BrushTip(float2 uv) or float4 BrushTip(float2 uv, DynamicBrushContext brush).");
                definitions++; dynamic = dyn;
            }
            if (definitions != 1) throw new FormatException("Define exactly one BrushTip entry point: Static or Dynamic, not both.");
            return dynamic;
        }

        internal static string ReadName(string first)
        {
            var header=Regex.Match(first?.TrimStart('\uFEFF')??"", @"^//\s*@whimtex-brush\s+([^\r\n]+?)\s*$");
            if(!header.Success) throw new FormatException("Line 1: expected // @whimtex-brush Category/Name.");
            string name=header.Groups[1].Value.Trim();
            foreach(var segment in name.Split('/'))
                if(string.IsNullOrWhiteSpace(segment))throw new FormatException("Brush name contains an empty path segment.");
            return name;
        }

        internal static List<ShaderFXParameter> Parse(string source, out string name)
        {
            if (string.IsNullOrEmpty(source) || Encoding.UTF8.GetByteCount(source)>65536)
                throw new FormatException("Brush HLSL must contain 1..65536 UTF-8 bytes.");
            using var reader = new StringReader(source);
            name=ReadName(reader.ReadLine());
            ReadDynamic(source);
            bool block=false;
            using(var lines=new StringReader(source))
            {
                string line;
                while((line=lines.ReadLine())!=null)
                    if(ShaderFXSourceBuilder.MaskComments(line,ref block).Contains("#"))
                        throw new FormatException("Brush HLSL must be self-contained: preprocessor directives and includes are not supported.");
            }
            var parameters=ShaderFXMetadata.Parse(source,false,out _);
            if(parameters.Count>32)throw new FormatException("A brush supports at most 32 parameters.");
            foreach(var p in parameters)
            {
                if (p.type == ShaderFXParameterType.Texture2D &&
                    (p.textureSource == ShaderFXTextureSource.Layer || p.textureSource == ShaderFXTextureSource.Self))
                    throw new FormatException("Brush texture parameters support Texture and None, not Layer or Self.");
                if(p.name=="BrushTip" || p.name=="BrushRandom" || p.name=="DynamicBrushContext" || p.name.StartsWith("_WhimTex_",StringComparison.Ordinal))
                    throw new FormatException("Reserved brush parameter name: "+p.name);
            }
            ShaderFXSourceBuilder.BuildParameterDeclarations(parameters, new StringBuilder(), new StringBuilder(), new HashSet<string>(StringComparer.Ordinal));
            return parameters;
        }

        internal Texture2D Bake(string source, List<ShaderFXParameter> values, int resolution)
        {
            if(resolution<32 || resolution>2048)throw new FormatException("Tip resolution must be 32..2048.");
            var declarations=Parse(source,out _);
            if (ReadDynamic(source)) throw new FormatException("Dynamic brush tips are evaluated while painting, not baked. Use PrepareDynamic.");
            ShaderFXMetadata.PreserveValues(declarations,values);
            if(material==null || compiledSource!=source)
            {
                var properties = new StringBuilder();
                var uniforms = new StringBuilder();
                var aliases = Aliases(declarations);
                ShaderFXSourceBuilder.BuildParameterDeclarations(aliases, properties, uniforms, new HashSet<string>(StringComparer.Ordinal), true);
                string wrapped = "Shader \"Hidden/WhimTex/BrushTip\" { Properties {\n" + properties +
                    "} SubShader { Pass { ZTest Always Cull Off ZWrite Off\nHLSLPROGRAM\n#pragma vertex vert_img\n#pragma fragment WhimTexBrushFragment\n#pragma target 3.5\n#include \"UnityCG.cginc\"\n" +
                    ShaderFXSourceBuilder.NoiseLibraryInclude + uniforms + AliasSource(source, declarations) +
                    "\nfloat4 WhimTexBrushFragment(v2f_img i) : SV_Target { float4 c=BrushTip(i.uv); return float4(clamp(c.rgb,-65504,65504),saturate(c.a)); }\nENDHLSL\n} } }";
                Compile(wrapped, source, false, declarations);
                compiledParameters = aliases;
            }
            ApplyValues(declarations, new Vector2(resolution, resolution));
            var previous=RenderTexture.active; bool srgb=GL.sRGBWrite;
            RenderTexture target=null; Texture2D result=null;
            try
            {
                target=RenderTexture.GetTemporary(resolution,resolution,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
                GL.sRGBWrite=false; Graphics.Blit(null,target,material);
                RenderTexture.active=target;
                result=new Texture2D(resolution,resolution,TextureFormat.RGBAHalf,false,true)
                    {name="HLSL Brush Tip",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                result.ReadPixels(new Rect(0,0,resolution,resolution),0,0); result.Apply(false,false);
                var ready=result; result=null; return ready;
            }
            finally
            {
                RenderTexture.active=previous; GL.sRGBWrite=srgb;
                if(target!=null)RenderTexture.ReleaseTemporary(target);
                if(result!=null)UnityEngine.Object.DestroyImmediate(result);
            }
        }

        internal void PrepareDynamic(string source, List<ShaderFXParameter> values)
        {
            var declarations = Parse(source, out _);
            if (!ReadDynamic(source)) throw new FormatException("PrepareDynamic requires DynamicBrushContext in the BrushTip signature.");
            ShaderFXMetadata.PreserveValues(declarations, values);
            if (!Matches(source))
            {
                var properties = new StringBuilder();
                var uniforms = new StringBuilder();
                var aliases = Aliases(declarations);
                ShaderFXSourceBuilder.BuildParameterDeclarations(aliases, properties, uniforms, new HashSet<string>(StringComparer.Ordinal), true);
                string body = AliasSource(source, declarations);
                const string root = "Packages/com.dcfapixels.whimtex/src/Shaders/";
                string template = File.ReadAllText(PresetLibraryPaths.PhysicalPath(root + "PaintBrush.shader"));
                string wrapped = template.Replace("Hidden/WhimTex/PaintBrush", "Hidden/WhimTex/DynamicBrush")
                    .Replace("#pragma target 3.0", "#pragma target 3.5")
                    .Replace("#pragma multi_compile_local __ BRUSH_DYNAMICS BRUSH_TEXTURE", "#define BRUSH_TEXTURE 1")
                    .Replace("#include \"HdrColor.cginc\"", "#include \"" + root + "HdrColor.cginc\"")
                    .Replace("#include \"ColorBlend.cginc\"", "#include \"" + root + "ColorBlend.cginc\"")
                    .Replace("// WHIMTEX_BRUSH_PROGRAM", "#define BRUSH_HLSL 1\n" + ShaderFXSourceBuilder.NoiseLibraryInclude +
                        File.ReadAllText(PresetLibraryPaths.PhysicalPath(root + "DynamicBrushContext.cginc")) + uniforms + body);
                wrapped = Regex.Replace(wrapped, @"\bProperties\s*\{", m => m.Value + "\n" + properties, RegexOptions.None);
                Compile(wrapped, source, true, declarations);
                compiledParameters = aliases;
            }
            ApplyValues(declarations, Vector2.one);
        }

        private static List<ShaderFXParameter> Aliases(IReadOnlyList<ShaderFXParameter> parameters)
        {
            var result = new List<ShaderFXParameter>(parameters.Count);
            foreach (var parameter in parameters)
            {
                var copy = parameter.Copy();
                copy.name = "_WhimTex_TipParameter" + parameter.name;
                result.Add(copy);
            }
            return result;
        }

        private static string AliasSource(string source, IReadOnlyList<ShaderFXParameter> parameters)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in parameters)
            {
                string alias = "_WhimTex_TipParameter" + p.name;
                names.Add(p.name, alias);
                if (p.type == ShaderFXParameterType.Gradient || p.type == ShaderFXParameterType.Curve) names.Add(p.name + "_Sample", alias + "_Sample");
                if (p.type == ShaderFXParameterType.Texture2D) names.Add(p.name + "_TexelSize", alias + "_TexelSize");
                if (p.type == ShaderFXParameterType.Transform2D)
                {
                    names.Add(p.name + "_ToLocal", alias + "_ToLocal");
                    names.Add(p.name + "_ToInput", alias + "_ToInput");
                }
            }
            return Regex.Replace(source, @"\b[A-Za-z_][A-Za-z0-9_]*\b", m => names.TryGetValue(m.Value, out var alias) ? alias : m.Value);
        }

        private void ApplyValues(List<ShaderFXParameter> declarations, Vector2 dimensions)
        {
            parameterBindings ??= new ShaderParameterBindings();
            for (int i = 0; i < declarations.Count; i++)
            {
                var p = declarations[i];
                if (p.type == ShaderFXParameterType.Texture2D &&
                    (p.textureSource == ShaderFXTextureSource.Layer || p.textureSource == ShaderFXTextureSource.Self))
                    throw new FormatException("Brush texture parameters support Texture and None, not Layer or Self.");
                if (ShaderFXMetadata.IsScalar(p.type)) p.floatValue = p.Clamp(p.floatValue);
                parameterBindings.Apply(material, p, compiledParameters[i], dimensions);
            }
        }

        private void Compile(string wrapped, string source, bool dynamic, IReadOnlyList<ShaderFXParameter> parameters)
        {
            Shader candidate = null; Material next = null;
            try
            {
                candidate = ShaderUtil.CreateShaderAsset(wrapped, true);
                if (candidate == null) throw new InvalidOperationException("Could not compile brush.");
                candidate.hideFlags = HideFlags.HideAndDontSave;
                next = new Material(candidate) { hideFlags = HideFlags.HideAndDontSave };
                ShaderUtil.CompilePass(next, 0, true);
                var diagnostics = ShaderFXDiagnostics.CollectShader(candidate, wrapped, source, parameters, "HLSL Brush",
                    candidate.isSupported && next.passCount > 0, false);
                if (ShaderFXDiagnostics.HasErrors(diagnostics))
                    throw new InvalidOperationException(ShaderFXDiagnostics.Format(diagnostics));
                Dispose(); shader = candidate; material = next; compiledSource = source; IsDynamic = dynamic;
                Diagnostics = ShaderFXDiagnostics.Format(diagnostics);
                ShaderFXDiagnostics.Report(diagnostics, "HLSL Brush");
                candidate = null; next = null;
            }
            finally
            {
                if (next != null) UnityEngine.Object.DestroyImmediate(next);
                if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
            }
        }
        public void Dispose()
        {
            parameterBindings?.Dispose(); parameterBindings = null; compiledParameters = null;
            if(material!=null)UnityEngine.Object.DestroyImmediate(material);
            if(shader!=null)UnityEngine.Object.DestroyImmediate(shader);
            material=null;shader=null;compiledSource=null;IsDynamic=false;Diagnostics=null;
        }
        internal static string Export(string source,List<ShaderFXParameter> values)
        {
            var parameters = Parse(source, out string name);
            ShaderFXMetadata.PreserveValues(parameters, values);
            string result = ShaderFXPresetWriter.BuildParameterSource(source, parameters, name, "whimtex-brush");
            Parse(result, out _);
            return result;
        }
    }
}
