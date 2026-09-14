Shader "Hidden/TextureCompositor/Shape"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            float2 _CanvasSize, _ShapePivot, _ShapePosition, _ShapeScale;
            float _ShapeRotation;
            int _ShapeKind, _ShapeTiling, _ShapeVertexCount;
            float4 _ShapeVertices[64];
            float4 _ShapeFill, _ShapeStroke, _ShapeStyle, _ShapeCorners;

            float BoxDistance(float2 p, float2 halfSize, float radius)
            {
                float2 q = abs(p) - halfSize + radius;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
            }

            float PolygonDistance(float2 p, float2 halfSize)
            {
                int count = _ShapeVertexCount;
                float distanceSquared = 1e30;
                bool inside = false;
                float2 a = _ShapeVertices[count - 1].xy * halfSize;
                [loop] for (int index = 0; index < count; index++)
                {
                    float2 b = _ShapeVertices[index].xy * halfSize;
                    float2 edge = b - a;
                    float2 nearest = a + edge * saturate(dot(p - a, edge) / max(dot(edge, edge), 1e-12));
                    distanceSquared = min(distanceSquared, dot(p - nearest, p - nearest));
                    if ((a.y > p.y) != (b.y > p.y))
                    {
                        float x = a.x + (p.y - a.y) * edge.x / edge.y;
                        if (p.x < x) inside = !inside;
                    }
                    a = b;
                }
                return sqrt(distanceSquared) * (inside ? -1 : 1);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                if (any(abs(_ShapeScale) < 1e-6)) return 0;
                float2 local = input.uv * _CanvasSize - _ShapePivot * _CanvasSize - _ShapePosition;
                float sine = sin(-_ShapeRotation), cosine = cos(-_ShapeRotation);
                local = float2(cosine * local.x - sine * local.y, sine * local.x + cosine * local.y);
                float2 uv = (_ShapePivot * _CanvasSize + local / _ShapeScale) / _CanvasSize;
                // TransformTilingMode: Clip, Repeat, Mirror, Source (clamp).
                if (_ShapeTiling == 1) uv = frac(uv);
                else if (_ShapeTiling == 2) uv = 1 - abs(frac(uv * .5) * 2 - 1);
                else if (_ShapeTiling == 3 || _ShapeTiling == 4) uv = saturate(uv);
                float2 halfSize = max(abs(_ShapeScale) * _CanvasSize * .5, 1e-5);
                float2 p = (uv - .5) * halfSize * 2;
                float distance;
                if (_ShapeKind == 0)
                {
                    float radius = p.y >= 0 ? (p.x < 0 ? _ShapeCorners.x : _ShapeCorners.y)
                        : (p.x < 0 ? _ShapeCorners.w : _ShapeCorners.z);
                    distance = BoxDistance(p, halfSize, min(halfSize.x, halfSize.y) * radius);
                }
                else if (_ShapeKind == 1)
                {
                    float k = length(p / halfSize);
                    float gradient = length(p / (halfSize * halfSize));
                    distance = k < 1e-6 ? -min(halfSize.x, halfSize.y) : (k - 1) * k / max(gradient, 1e-8);
                }
                else if (_ShapeKind == 4) distance = BoxDistance(p, halfSize, min(halfSize.x, halfSize.y));
                else distance = PolygonDistance(p, halfSize);
                float aa = max(fwidth(distance), 1e-4);
                float outer = saturate(.5 - distance / aa);
                float inner = saturate(.5 - (distance + _ShapeStyle.z) / aa);
                float strokeCoverage = (outer - inner) * _ShapeStyle.y;
                float fillCoverage = (outer - strokeCoverage) * _ShapeStyle.x;
                float fillAlpha = fillCoverage * saturate(_ShapeFill.a);
                float strokeAlpha = strokeCoverage * saturate(_ShapeStroke.a);
                float alpha = fillAlpha + strokeAlpha;
                float3 rgb = (_ShapeFill.rgb * fillAlpha + _ShapeStroke.rgb * strokeAlpha) / max(alpha, 1e-8);
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
}
