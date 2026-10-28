using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXCodeSyntax
    {
        internal enum Kind : byte { Plain, Keyword, Type, Number, String, Comment, Directive, Parameter, Function }
        sealed class Word
        {
            internal string text;
            internal Kind kind;
            internal bool metadata;
            internal Word next;
        }
        static readonly Dictionary<uint, Word> Words = CreateWords();
        string source;
        byte[] kinds = Array.Empty<byte>();
        internal int Revision { get; private set; }
        static uint Hash(string text, int start, int end)
        {
            uint hash = 2166136261;
            unchecked { for (int i = start; i < end; i++) hash = (hash ^ text[i]) * 16777619; }
            return hash;
        }
        static Dictionary<uint, Word> CreateWords()
        {
            var result = new Dictionary<uint, Word>();
            void Add(string text, Kind kind, bool metadata = false)
            {
                uint hash = Hash(text, 0, text.Length);
                result.TryGetValue(hash, out Word previous);
                result[hash] = new Word { text = text, kind = kind, metadata = metadata, next = previous };
            }
            foreach (string word in "break case cbuffer centroid class column_major const continue default discard do else extern false for groupshared if in inline inout interface namespace nointerpolation out precise register return row_major shared static struct switch true typedef uniform volatile while".Split(' ')) Add(word, Kind.Keyword);
            foreach (string word in "Buffer ByteAddressBuffer InputPatch OutputPatch PinnedUV WhimTexTexture RWBuffer RWByteAddressBuffer RWStructuredBuffer RWTexture1D RWTexture2D RWTexture3D SamplerComparisonState SamplerState StructuredBuffer Texture1D Texture1DArray Texture2D Texture2DArray Texture2DMS Texture3D TextureCube TextureCubeArray matrix sampler sampler1D sampler2D sampler3D samplerCUBE string vector void".Split(' ')) Add(word, Kind.Type);
            foreach (string scalar in "bool double fixed float half int min10float min12int min16float min16int min16uint uint dword".Split(' '))
            {
                Add(scalar, Kind.Type);
                for (char x = '1'; x <= '4'; x++)
                {
                    Add(scalar + x, Kind.Type);
                    for (char y = '1'; y <= '4'; y++) Add(scalar + x + "x" + y, Kind.Type);
                }
            }
            foreach (string word in "color curve enum gradient normal point seed texture2D toggle transform2D".Split(' ')) Add(word, Kind.Type, true);
            foreach (string word in "hidden label preview px rgba".Split(' ')) Add(word, Kind.Directive, true);
            return result;
        }

        internal bool Update(string text, WhimTexDocument document = null)
        {
            text ??= string.Empty;
            if (string.Equals(source, text, StringComparison.Ordinal)) return false;
            using var timing = WhimTexProfiler.Measure("ui.code.tokenize", document, detail: "HLSL");
            source = text;
            if (kinds.Length < text.Length) Array.Resize(ref kinds, Mathf.NextPowerOfTwo(Math.Max(256, text.Length)));
            Array.Clear(kinds, 0, text.Length);
            Revision++;
            for (int i = 0; i < text.Length;)
            {
                int start = i;
                char c = text[i], next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    i += 2;
                    while (i < text.Length && text[i] != '\r' && text[i] != '\n') i++;
                    Paint(start, i, Kind.Comment);
                    int at = start + 2;
                    while (at < i && char.IsWhiteSpace(text[at])) at++;
                    if (at < i && text[at] == '@') Metadata(at, i);
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    i += 2;
                    while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')) i++;
                    i = Math.Min(text.Length, i + 2);
                    Paint(start, i, Kind.Comment); continue;
                }
                if (c == '"' || c == '\'')
                {
                    i = Quoted(i, text.Length); Paint(start, i, Kind.String); continue;
                }
                if (c == '#' && LinePrefix(start))
                {
                    i++;
                    while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
                    while (i < text.Length && Identifier(text[i])) i++;
                    Paint(start, i, Kind.Directive); continue;
                }
                if (char.IsDigit(c) || c == '.' && char.IsDigit(next))
                {
                    i = Number(i, text.Length); Paint(start, i, Kind.Number); continue;
                }
                if (IdentifierStart(c))
                {
                    i++; while (i < text.Length && Identifier(text[i])) i++;
                    Kind kind = Classify(start, i);
                    if (kind == Kind.Plain)
                    {
                        int after = i;
                        while (after < text.Length && char.IsWhiteSpace(text[after])) after++;
                        if (after < text.Length && text[after] == '(') kind = Kind.Function;
                    }
                    Paint(start, i, kind); continue;
                }
                i++;
            }
            return true;
        }

        internal Kind At(int index) => source != null && (uint)index < (uint)source.Length ? (Kind)kinds[index] : Kind.Plain;
        void Paint(int start, int end, Kind kind) { for (int i = start; i < end; i++) kinds[i] = (byte)kind; }
        static bool IdentifierStart(char c) => c == '_' || char.IsLetter(c);
        static bool Identifier(char c) => IdentifierStart(c) || char.IsDigit(c);
        bool LinePrefix(int at)
        {
            while (--at >= 0 && source[at] != '\n' && source[at] != '\r')
                if (source[at] != ' ' && source[at] != '\t') return false;
            return true;
        }
        int Quoted(int at, int end)
        {
            char quote = source[at++];
            while (at < end)
            {
                char c = source[at++];
                if (c == '\\' && at < end) at++;
                else if (c == quote || c == '\n' || c == '\r') break;
            }
            return at;
        }
        int Number(int at, int end)
        {
            if (source[at] == '0' && at + 1 < end && (source[at + 1] == 'x' || source[at + 1] == 'X'))
            {
                at += 2;
                while (at < end && (char.IsDigit(source[at]) || source[at] >= 'a' && source[at] <= 'f' || source[at] >= 'A' && source[at] <= 'F')) at++;
            }
            else
            {
                while (at < end && char.IsDigit(source[at])) at++;
                if (at < end && source[at] == '.') { at++; while (at < end && char.IsDigit(source[at])) at++; }
                if (at < end && (source[at] == 'e' || source[at] == 'E'))
                {
                    at++;
                    if (at < end && (source[at] == '+' || source[at] == '-')) at++;
                    while (at < end && char.IsDigit(source[at])) at++;
                }
            }
            while (at < end && "fFhHuUlL".IndexOf(source[at]) >= 0) at++;
            return at;
        }
        void Metadata(int at, int end)
        {
            while (at < end)
            {
                int start = at;
                char c = source[at];
                if (c == '@')
                {
                    at++; while (at < end && (Identifier(source[at]) || source[at] == '-')) at++;
                    Paint(start, at, Kind.Directive);
                }
                else if (c == '"' || c == '\'') { at = Quoted(at, end); Paint(start, at, Kind.String); }
                else if (char.IsDigit(c) || c == '.' && at + 1 < end && char.IsDigit(source[at + 1]))
                { at = Number(at, end); Paint(start, at, Kind.Number); }
                else if (IdentifierStart(c))
                {
                    at++; while (at < end && Identifier(source[at])) at++;
                    Kind kind = Classify(start, at, true);
                    if (kind != Kind.Plain) Paint(start, at, kind);
                }
                else at++;
            }
        }
        Kind Classify(int start, int end, bool metadata = false)
        {
            if (source[start] == '_') return Kind.Parameter;
            Words.TryGetValue(Hash(source, start, end), out Word word);
            for (; word != null; word = word.next)
            {
                if (end - start != word.text.Length || !source.AsSpan(start, end - start).SequenceEqual(word.text.AsSpan())) continue;
                return !word.metadata || metadata ? word.kind : Kind.Plain;
            }
            return Kind.Plain;
        }

#if UNITY_6000_7_OR_NEWER
        readonly TextElement element;
        readonly TextField field;
        readonly ITextSelection selection;
        readonly ShaderFX effect;
        readonly Color32[] palette;
        ScrollView scroll;
        internal ShaderFXCodeSyntax(TextField field, ShaderFX effect)
        {
            this.field = field;
            this.effect = effect;
            element = (TextElement)field.textEdition;
            selection = field.textSelection;
            palette = EditorGUIUtility.isProSkin
                ? new Color32[] { default, new Color32(197, 134, 192, 255), new Color32(86, 156, 214, 255), new Color32(181, 206, 168, 255), new Color32(206, 145, 120, 255), new Color32(106, 153, 85, 255), new Color32(220, 160, 225, 255), new Color32(156, 220, 254, 255), new Color32(220, 220, 170, 255) }
                : new Color32[] { default, new Color32(128, 32, 144, 255), new Color32(0, 65, 160, 255), new Color32(30, 105, 65, 255), new Color32(155, 55, 20, 255), new Color32(50, 112, 40, 255), new Color32(145, 30, 130, 255), new Color32(0, 95, 135, 255), new Color32(115, 80, 10, 255) };
            element.enableRichText = false;
            element.PostProcessTextVertices += Colorize;
            field.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (evt.target != field) return;
                scroll = field.Q<ScrollView>();
                if (scroll == null) return;
                scroll.verticalScroller.valueChanged += OnScroll;
                scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(OnViewportChanged);
            });
            field.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target != field || scroll == null) return;
                scroll.verticalScroller.valueChanged -= OnScroll;
                scroll.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnViewportChanged);
                scroll = null;
            });
        }
        void OnScroll(float _) => element.MarkDirtyRepaint();
        void OnViewportChanged(GeometryChangedEvent _) => element.MarkDirtyRepaint();
        internal Rect VisibleRect()
        {
            Rect bounds = field.worldBound;
            if (scroll != null)
            {
                Rect viewport = scroll.contentViewport.worldBound;
                bounds = Rect.MinMaxRect(Math.Max(bounds.xMin, viewport.xMin), Math.Max(bounds.yMin, viewport.yMin),
                    Math.Min(bounds.xMax, viewport.xMax), Math.Min(bounds.yMax, viewport.yMax));
            }
            Rect clip = element.WorldToLocal(bounds);
            float margin = Math.Max(2, element.resolvedStyle.fontSize * 2);
            clip.yMin -= margin; clip.yMax += margin;
            return clip;
        }
        void Colorize(TextElement.GlyphsEnumerable glyphs)
        {
            using var timing = WhimTexProfiler.Measure("ui.code.color", effect != null ? effect.EmbeddedOwner : null, detail: "HLSL");
            Update(element.text, effect != null ? effect.EmbeddedOwner : null);
            int low = Math.Min(selection.cursorIndex, selection.selectIndex), high = Math.Max(selection.cursorIndex, selection.selectIndex);
            bool enabled = element.enabledInHierarchy;
            Rect clip = VisibleRect();
            int belowLine = -1;
            foreach (var glyph in glyphs)
            {
                if (belowLine >= 0 && glyph.line > belowLine) break;
                var vertices = glyph.vertices;
                if (vertices.Length < 2) continue;
                float top = Math.Min(vertices[0].position.y, vertices[1].position.y);
                float bottom = Math.Max(vertices[0].position.y, vertices[1].position.y);
                if (top > clip.yMax) { belowLine = glyph.line; continue; }
                if (bottom < clip.yMin) continue;
                RangeInt range = glyph.textRange;
                if (range.length <= 0 || range.start < 0 || range.start >= source.Length || range.start < high && range.end > low) continue;
                Kind kind = At(range.start);
                if (kind == Kind.Plain) continue;
                Color32 tint = palette[(int)kind];
                for (int i = 0; i < vertices.Length; i++)
                {
                    var vertex = vertices[i];
                    Color32 color = tint;
                    if (!enabled) color = Color.Lerp(vertex.tint, tint, .4f);
                    color.a = vertex.tint.a;
                    vertex.tint = color;
                    vertices[i] = vertex;
                }
            }
        }
#else
        internal ShaderFXCodeSyntax(TextField field, ShaderFX effect) { }
#endif
    }
}
