using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Per-evaluation inputs, never global shader state or serialized document parameters.
    internal readonly struct ShaderEvaluationContext
    {
        internal readonly float Time;
        internal readonly uint InstanceIndex;
        internal ShaderEvaluationContext(float time, uint instanceIndex = 0)
        { Time = time; InstanceIndex = instanceIndex; }

        internal const string Declarations = "float _WhimTexTime;\nuint _WhimTexInstanceIndex;\n";
        static readonly int TimeId = Shader.PropertyToID("_WhimTexTime");
        static readonly int IndexId = Shader.PropertyToID("_WhimTexInstanceIndex");
        internal void Bind(Material material)
        {
            material.SetFloat(TimeId, Time);
            material.SetInteger(IndexId, unchecked((int)InstanceIndex));
        }
    }

    [Flags]
    internal enum ShaderContextDependencies { None = 0, Time = 1, InstanceIndex = 2, Lighting = 4, All = Time | InstanceIndex | Lighting }

    internal static class ShaderContextUsage
    {
        const string MarkerPrefix = "// WhimTex context inputs: ";
        internal static string Marker(ShaderContextDependencies value) => MarkerPrefix + (int)value + "\n";
        // Analyze expanded author source, before injecting built-in declarations. This is
        // conservative: inactive branches/helpers may count, token pasting or unresolved
        // includes count as both inputs. Never infer independence from an opaque dependency.
        internal static ShaderContextDependencies Analyze(string source)
        {
            string clean = ShaderEntryPoint.Clean(Regex.Replace(source ?? "", @"\\(?:\r\n|\r|\n)", ""));
            return AnalyzeClean(clean);
        }
        internal static ShaderContextDependencies AnalyzeDocument(ShaderSourceDocument document)
        {
            // Splicing before comment removal matters for split identifiers and comments.
            return document.Source.IndexOf("\\\n", StringComparison.Ordinal) >= 0 || document.Source.IndexOf("\\\r", StringComparison.Ordinal) >= 0
                ? Analyze(document.Source) : AnalyzeClean(document.Clean);
        }
        static ShaderContextDependencies AnalyzeClean(string clean)
        {
            var result = ShaderContextDependencies.None;
            for (int i = 0; i < clean.Length;)
            {
                char c = clean[i];
                if (c == '#')
                {
                    i++;
                    while (i < clean.Length && char.IsWhiteSpace(clean[i])) i++;
                    if (i < clean.Length && clean[i] == '#') return ShaderContextDependencies.All;
                    int begin = i;
                    while (i < clean.Length && Identifier(clean[i])) i++;
                    var directive = clean.AsSpan(begin, i - begin);
                    if (directive.SequenceEqual("include".AsSpan()) || directive.SequenceEqual("include_with_pragmas".AsSpan()))
                        return ShaderContextDependencies.All;
                    continue;
                }
                if (!Identifier(c)) { i++; continue; }
                int start = i++;
                while (i < clean.Length && Identifier(clean[i])) i++;
                var token = clean.AsSpan(start, i - start);
                if (token.SequenceEqual("_WhimTexTime".AsSpan())) result |= ShaderContextDependencies.Time;
                else if (token.SequenceEqual("_WhimTexInstanceIndex".AsSpan())) result |= ShaderContextDependencies.InstanceIndex;
                else if (token.SequenceEqual("WTLightSampleCount".AsSpan()) || token.SequenceEqual("WTLightCount".AsSpan()) || token.SequenceEqual("WTSampleLight".AsSpan()) || token.SequenceEqual("WTAmbientColor".AsSpan()) ||
                    token.SequenceEqual("WTGlobalLighting".AsSpan()) || token.SequenceEqual("WhimTexGlobalSurfaceLighting".AsSpan()))
                    result |= ShaderContextDependencies.Lighting;
            }
            return result;
        }
        static bool Identifier(char c) => c == '_' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9';
    }
}
