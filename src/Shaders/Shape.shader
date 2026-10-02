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
            float2 _CanvasSize, _ShapeScale;
            float3 _ShapeRow0, _ShapeRow1, _ShapeRow2;
            int _ShapeKind, _ShapeTiling, _ShapeVertexCount;
            float4 _ShapeVertices[64];
            float4 _ShapeFill, _ShapeStroke, _ShapeStyle, _ShapeCorners;
            float2 _ShapeFeather;

            float EllipseDistance(float2 p, float2 halfSize)
            {
                p = abs(p);
                if (halfSize.x < halfSize.y) { halfSize = halfSize.yx; p = p.yx; }
                if (abs(halfSize.x - halfSize.y) < 1e-5) return length(p) - halfSize.x;
                float lo = 0, hi = UNITY_PI * .5;
                [unroll] for (int i = 0; i < 20; i++)
                {
                    float angle = (lo + hi) * .5;
                    float s, c;
                    sincos(angle, s, c);
                    float derivative = (halfSize.y * halfSize.y - halfSize.x * halfSize.x) * s * c
                        + halfSize.x * p.x * s - halfSize.y * p.y * c;
                    if (derivative > 0) hi = angle;
                    else lo = angle;
                }
                float s, c;
                sincos((lo + hi) * .5, s, c);
                float result = length(p - halfSize * float2(c, s));
                return dot(p / halfSize, p / halfSize) < 1 ? -result : result;
            }

            float FeatherCoverage(float distance, float aa)
            {
                float width = _ShapeFeather.x;
                float soft = 1 - smoothstep(-width * (1 - _ShapeFeather.y), width * _ShapeFeather.y, distance);
                float sharp = saturate(.5 - distance / aa);
                return lerp(sharp, soft, saturate(width / aa));
            }

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
                float3 q=float3(input.uv,1);
                float w=dot(_ShapeRow2,q);
                if(w<1e-8)return 0;
                float2 uv=float2(dot(_ShapeRow0,q),dot(_ShapeRow1,q))/w;
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
                    if (_ShapeFeather.x > 0) distance = EllipseDistance(p, halfSize);
                }
                else if (_ShapeKind == 4) distance = BoxDistance(p, halfSize, min(halfSize.x, halfSize.y));
                else distance = PolygonDistance(p, halfSize);
                float aa = max(fwidth(distance), 1e-4);
                float outer = saturate(.5 - distance / aa);
                float inner = saturate(.5 - (distance + _ShapeStyle.z) / aa);
                float strokeCoverage = (outer - inner) * _ShapeStyle.y;
                if (_ShapeFeather.x > 0)
                {
                    outer = FeatherCoverage(distance, aa);
                    strokeCoverage = min(outer, FeatherCoverage(-distance - _ShapeStyle.z, aa)) * _ShapeStyle.y;
                    if (_ShapeStyle.z <= 0) strokeCoverage = 0;
                }
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
