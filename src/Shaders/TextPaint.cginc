#include "UnityCG.cginc"
#include "GradientPalette.cginc"
#include "FigureGradient.cginc"
sampler2D _MainTex;
float2 _TextCanvasSize, _TextBoldOffset;
float4 _TextClipRect, _TextPaintRect, _TextAnchors;
float _TextRepetitions;
int _TextPixel, _TextClip, _TextFillMode;
struct TextPaintVertex { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 paint : TEXCOORD1; };
struct TextPaintFragment { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float2 pixel : TEXCOORD1; float4 paint : TEXCOORD2; };
TextPaintFragment TextPaintVert(TextPaintVertex v)
{
    TextPaintFragment o;
    o.vertex = float4(v.vertex.xy * 2 / _TextCanvasSize, 0, 1);
    #if UNITY_UV_STARTS_AT_TOP
    o.vertex.y = -o.vertex.y;
    #endif
    o.uv = v.uv; o.pixel = v.vertex.xy; o.paint = v.paint; return o;
}
float4 TextPaintColor(float2 uv, float2 size)
{
    return SampleGradientPalette(FigureGradientCoordinate(uv, size, _TextAnchors, _TextFillMode, _TextRepetitions));
}
float4 TextPaintWhole(v2f_img i) : SV_Target
{
    float2 uv = (i.uv * _TextCanvasSize - _TextPaintRect.xy) / _TextPaintRect.zw;
    float4 color = TextPaintColor(uv, _TextPaintRect.zw);
    return float4(color.rgb, color.a * tex2D(_MainTex, i.uv).a);
}
float4 TextPaintGlyph(TextPaintFragment i) : SV_Target
{
    if (_TextClip) clip(min(min(i.pixel.x - _TextClipRect.x, _TextClipRect.z - i.pixel.x), min(i.pixel.y - _TextClipRect.y, _TextClipRect.w - i.pixel.y)));
    float coverage = tex2D(_MainTex, i.uv).a;
    if (_TextBoldOffset.x > 0)
    {
        coverage = max(coverage, tex2D(_MainTex, i.uv + float2(_TextBoldOffset.x, 0)).a);
        coverage = max(coverage, tex2D(_MainTex, i.uv - float2(_TextBoldOffset.x, 0)).a);
        coverage = max(coverage, tex2D(_MainTex, i.uv + float2(0, _TextBoldOffset.y)).a);
        coverage = max(coverage, tex2D(_MainTex, i.uv - float2(0, _TextBoldOffset.y)).a);
    }
    if (_TextPixel) coverage = step(.5, coverage);
    float4 color = TextPaintColor(i.paint.xy, i.paint.zw);
    color.a *= coverage;
    return float4(color.rgb * color.a, color.a);
}
