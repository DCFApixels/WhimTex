float4 GraphBlend(float4 background, float4 foreground, float opacity, float mode, float hdr)
{
    if (mode == 29) return opacity <= 0 ? background : CrossfadePixels(background, foreground, saturate(opacity));
    float oldHdr = _HdrBlend; _HdrBlend = hdr;
    float4 b = float4(background.rgb*background.a, background.a);
    float4 f = float4(foreground.rgb*foreground.a, foreground.a);
    float4 c = CompositeBrushPixel(b,f,opacity,mode,0,1-hdr);
    _HdrBlend = oldHdr;
    return float4(c.a > 1e-7 ? c.rgb/c.a : background.rgb,c.a);
}
