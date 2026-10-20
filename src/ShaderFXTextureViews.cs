using System;
using System.Text;
using System.Text.RegularExpressions;

namespace DCFApixels.WhimTex
{
    internal static class ShaderFXTextureViews
    {
        internal static string Prefix(string name) => "_WhimTex_Texture" + name;

        // Keep the existing combined sampler representation: it includes the
        // texture's sampling state and works in the current CGPROGRAM wrapper.
        internal const string Types = @"
struct WhimTexTexture
{
    sampler2D pixels;
    float4 uvMap;
    float4 texelSize;
};
float2 _WhimTex_TextureUV(WhimTexTexture source, float2 uv)
{
    return uv * source.uvMap.xy + source.uvMap.zw;
}
float4 tex2D(WhimTexTexture source, float2 uv)
{
    return tex2D(source.pixels, _WhimTex_TextureUV(source, uv));
}
float4 tex2Dlod(WhimTexTexture source, float4 uv)
{
    return tex2Dlod(source.pixels, float4(_WhimTex_TextureUV(source, uv.xy), uv.zw));
}
float4 tex2Dbias(WhimTexTexture source, float4 uv)
{
    return tex2Dbias(source.pixels, float4(_WhimTex_TextureUV(source, uv.xy), uv.zw));
}
float4 tex2Dgrad(WhimTexTexture source, float2 uv, float2 dx, float2 dy)
{
    return tex2Dgrad(source.pixels, _WhimTex_TextureUV(source, uv), dx * source.uvMap.xy, dy * source.uvMap.xy);
}
float4 tex2D(WhimTexTexture source, float2 uv, float2 dx, float2 dy)
{
    return tex2Dgrad(source, uv, dx, dy);
}
float4 tex2Dproj(WhimTexTexture source, float4 uv)
{
    return tex2D(source, uv.xy / uv.w);
}
";

        // The macro creates a value, not a writable global struct. Ordinary HLSL
        // functions pass it by value without rewriting their calls or arguments.
        internal static void Declare(string name, StringBuilder properties, StringBuilder source)
        {
            string prefix = Prefix(name);
            properties.AppendLine($"[HideInInspector] {prefix}_UvMap (\"Input UV\", Vector) = (1,1,0,0)");
            properties.AppendLine($"[HideInInspector] {prefix}_Size (\"Logical Texel Size\", Vector) = (0,0,0,0)");
            source.AppendLine($"float4 {prefix}_UvMap, {prefix}_Size;");
            source.AppendLine($"WhimTexTexture {prefix}_Get() {{ WhimTexTexture _WhimTex_View; _WhimTex_View.pixels = {name}; _WhimTex_View.uvMap = {prefix}_UvMap; _WhimTex_View.texelSize = {prefix}_Size.z > 0 ? {prefix}_Size : {name}_TexelSize; return _WhimTex_View; }}");
            source.AppendLine($"#define {name} {prefix}_Get()");
            source.AppendLine($"#define {name}_TexelSize ({prefix}_Get().texelSize)");
        }

        internal static string Prepare(string source)
        {
            string clean = ShaderFXEntryPoint.Clean(source);
            if (Regex.IsMatch(clean, @"\b(?:struct|class|typedef)\b[^;{}\n]*\bWhimTexTexture\b|^\s*#\s*define[^\n]*\bWhimTexTexture\b", RegexOptions.Multiline))
                throw new FormatException("WhimTexTexture is reserved. Use declared texture inputs or pass/copy an existing WhimTexTexture.");
            if (Regex.IsMatch(clean, @"\bWhimTexTexture\s*\(|\(\s*(?:const\s+)?WhimTexTexture\s*\)"))
                throw new FormatException("WhimTexTexture cannot be constructed or cast. Use declared texture inputs.");
            var result = new StringBuilder(source);
            var declarations = Regex.Matches(clean, @"\bWhimTexTexture\s+([A-Za-z_]\w*)");
            for (int i = declarations.Count - 1; i >= 0; i--)
            {
                var match = declarations[i];
                int next = match.Index + match.Length;
                while (next < clean.Length && char.IsWhiteSpace(clean[next])) next++;
                if (next < clean.Length && clean[next] == '(') continue;
                string before = clean.Substring(0, match.Index);
                if (Regex.IsMatch(before, @"\b(?:out|inout)\s+(?:const\s+)?$"))
                    throw new FormatException("WhimTexTexture cannot be an out/inout parameter.");
                if (next < clean.Length && clean[next] == '=')
                {
                    next++;
                    while (next < clean.Length && char.IsWhiteSpace(clean[next])) next++;
                    if (next < clean.Length && clean[next] == '{')
                        throw new FormatException("WhimTexTexture cannot be created with a component initializer.");
                }
                if (!Regex.IsMatch(before, @"\bconst\s+(?:in\s+)?$")) result.Insert(match.Index, "const ");
            }
            return result.ToString();
        }
    }
}
