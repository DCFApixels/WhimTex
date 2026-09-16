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
    internal sealed class BrushTipProgram : IDisposable
    {
        internal const string DefaultSource = "// @whimtex-brush Shapes/Ring\n// @param float _Thickness = 0.15 [0.01 .. 0.5]\n\nfloat4 BrushTip(float2 uv)\n{\n    float d = abs(length(uv * 2.0 - 1.0) - 0.65);\n    float aa = max(fwidth(d), 0.001);\n    return float4(1, 1, 1, 1 - smoothstep(_Thickness, _Thickness + aa, d));\n}\n";
        internal static string Folder => Path.Combine(BrushPresetLibrary.Folder, "HLSL");
        private Shader shader;
        private Material material;
        private string compiledSource;

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
                if(p.controls.Count > 1 || p.controls.Exists(c => c.type == ShaderFXParameterType.Enum || c.type == ShaderFXParameterType.Bool))
                    throw new FormatException("Enum, bool and linked parameter controls are supported by FX, not brush HLSL.");
                if(p.type!=ShaderFXParameterType.Float && p.type!=ShaderFXParameterType.Color && p.type!=ShaderFXParameterType.Vector)
                    throw new FormatException("Brush parameters support float, float4 and color only.");
                if(p.name=="BrushTip" || p.name.StartsWith("_WhimTex_",StringComparison.Ordinal))
                    throw new FormatException("Reserved brush parameter name: "+p.name);
            }
            return parameters;
        }

        internal Texture2D Bake(string source, List<ShaderFXParameter> values, int resolution)
        {
            if(resolution<32 || resolution>2048)throw new FormatException("Tip resolution must be 32..2048.");
            var declarations=Parse(source,out _);
            ShaderFXMetadata.PreserveValues(declarations,values);
            if(material==null || compiledSource!=source)
            {
                var uniforms=new StringBuilder();
                foreach(var p in declarations)
                    uniforms.Append(p.type==ShaderFXParameterType.Float?"float ":"float4 ").Append(p.name).AppendLine(";");
                string wrapped="Shader \"Hidden/WhimTex/BrushTip\" { SubShader { Pass { ZTest Always Cull Off ZWrite Off\nHLSLPROGRAM\n#pragma vertex vert_img\n#pragma fragment WhimTexBrushFragment\n#pragma target 3.0\n#include \"UnityCG.cginc\"\n"+
                    ShaderFXSourceBuilder.NoiseLibraryInclude+uniforms+source+"\nfloat4 WhimTexBrushFragment(v2f_img i) : SV_Target { float4 c=BrushTip(i.uv); return float4(clamp(c.rgb,-65504,65504),saturate(c.a)); }\nENDHLSL\n} } }";
                Shader candidate=null; Material next=null;
                try
                {
                    candidate=ShaderUtil.CreateShaderAsset(wrapped,true);
                    if(candidate==null)throw new InvalidOperationException("Could not compile brush.");
                    candidate.hideFlags=HideFlags.HideAndDontSave;
                    next=new Material(candidate){hideFlags=HideFlags.HideAndDontSave};
                    ShaderUtil.CompilePass(next,0,true);
                    var errors=new StringBuilder();
                    foreach(var m in ShaderUtil.GetShaderMessages(candidate))
                        if(m.severity==ShaderCompilerMessageSeverity.Error)errors.AppendLine(m.message);
                    if(errors.Length>0 || !candidate.isSupported)throw new InvalidOperationException(errors.Length>0?errors.ToString():"Brush shader is not supported.");
                    Dispose(); shader=candidate; material=next; compiledSource=source; candidate=null;next=null;
                }
                finally
                {
                    if(next!=null)UnityEngine.Object.DestroyImmediate(next);
                    if(candidate!=null)UnityEngine.Object.DestroyImmediate(candidate);
                }
            }
            foreach(var p in declarations)
                if(p.type==ShaderFXParameterType.Float)material.SetFloat(p.name,p.Clamp(p.floatValue));
                else material.SetVector(p.name,p.type==ShaderFXParameterType.Color?(Vector4)p.colorValue:p.vectorValue);
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
        public void Dispose()
        {
            if(material!=null)UnityEngine.Object.DestroyImmediate(material);
            if(shader!=null)UnityEngine.Object.DestroyImmediate(shader);
            material=null;shader=null;compiledSource=null;
        }
        internal static string Export(string source,List<ShaderFXParameter> values)
        {
            var parameters=Parse(source,out string name);
            ShaderFXMetadata.PreserveValues(parameters,values);
            var result=new StringBuilder("// @whimtex-brush "+name+"\n");
            foreach(var p in parameters)
            {
                string declaration=ShaderFXPresetWriter.Declaration(p);
                if(p.controls.Count>0 && !string.IsNullOrEmpty(p.controls[0].tooltip))declaration+=" // "+p.controls[0].tooltip;
                result.AppendLine(declaration);
            }
            using var reader=new StringReader(source); reader.ReadLine();
            string line; bool block=false;
            while((line=reader.ReadLine())!=null)
            {
                bool declaration=!block && Regex.IsMatch(line,@"^\s*//\s*@param\b");
                ShaderFXSourceBuilder.MaskComments(line,ref block);
                if(!declaration)result.AppendLine(line);
            }
            return result.ToString();
        }
    }
}
