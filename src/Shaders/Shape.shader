Shader "Hidden/WhimTex/Shape"
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
            int _ShapeKind, _ShapeTiling, _ShapeSegmentCount, _ShapeLineCap, _ShapeEdgeMode;
            float4 _ShapeSegments[128], _ShapeCurves[128];
            float4 _ShapeFill, _ShapeStroke, _ShapeStyle;
            float _ShapeArcThickness;
            float2 _ShapeAngles;
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
                    float s = 0, c = 0;
                    sincos(angle, s, c);
                    float derivative = (halfSize.y * halfSize.y - halfSize.x * halfSize.x) * s * c
                        + halfSize.x * p.x * s - halfSize.y * p.y * c;
                    if (derivative > 0) hi = angle;
                    else lo = angle;
                }
                float s = 0, c = 0;
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

            float BoxDistance(float2 p, float2 halfSize)
            {
                float2 q = abs(p) - halfSize;
                return length(max(q, 0)) + min(max(q.x, q.y), 0);
            }

            float AngleProgress(float angle, float start, float sweep)
            {
                return frac((angle - start) * (sweep < 0 ? -1 : 1) / (UNITY_PI * 2)) * (UNITY_PI * 2);
            }
            bool InArc(float angle, float start, float sweep)
            { return abs(sweep) >= UNITY_PI * 2 - 1e-5 || AngleProgress(angle, start, sweep) <= abs(sweep) + 1e-5; }

            float2 ArcDistance(float2 p, float2 axes, float start, float sweep)
            {
                float2 a = axes * float2(cos(start), sin(start));
                float2 b = axes * float2(cos(start + sweep), sin(start + sweep));
                float da = dot(p - a, p - a), db = dot(p - b, p - b);
                float best = min(da, db);
                float nearestAngle = da <= db ? start : start + sweep;
                if (abs(axes.x - axes.y) < 1e-5)
                {
                    float angle = atan2(p.y, p.x);
                    if (InArc(angle, start, sweep))
                    {
                        float radial = length(p) - axes.x;
                        best = radial * radial; nearestAngle = angle;
                    }
                }
                else
                {
                    // All stationary candidates, including the far-side minima of a concave sector.
                    [unroll] for (int seed = 0; seed < 8; seed++)
                    {
                        float angle = (seed + .5) * (UNITY_PI * .25);
                        [unroll] for (int iteration = 0; iteration < 8; iteration++)
                        {
                            float s = 0, c = 0; sincos(angle, s, c);
                            float k = axes.y * axes.y - axes.x * axes.x;
                            float derivative = k * s * c + axes.x * p.x * s - axes.y * p.y * c;
                            float second = k * (c * c - s * s) + axes.x * p.x * c + axes.y * p.y * s;
                            angle -= clamp(derivative / (abs(second) > 1e-8 ? second : 1e-8), -.5, .5);
                        }
                        if (InArc(angle, start, sweep))
                        {
                            float2 delta = p - axes * float2(cos(angle), sin(angle));
                            float d = dot(delta, delta);
                            if (d < best) { best = d; nearestAngle = angle; }
                        }
                    }
                }
                return float2(sqrt(best), nearestAngle);
            }
            bool ArcCrossing(float2 p, float4 segment, float2 arc, float angle)
            {
                float progress = AngleProgress(angle, arc.x, arc.y);
                float derivative = cos(angle) * (arc.y < 0 ? -1 : 1);
                if (abs(derivative) < 1e-6 || !InArc(angle, arc.x, arc.y)) return false;
                if (abs(arc.y) < UNITY_PI * 2 - 1e-5)
                {
                    // Half-open Y intervals, matching the straight-segment crossing rule at joins.
                    if (progress < 1e-5 && derivative < 0) return false;
                    if (abs(progress - abs(arc.y)) < 1e-5 && derivative > 0) return false;
                }
                return p.x < segment.x + segment.z * cos(angle);
            }
            float ContourDistance(float2 p)
            {
                if (_ShapeSegmentCount == 0) return 1e10;
                float distance = 1e20;
                bool inside = false;
                [loop] for (int index = 0; index < _ShapeSegmentCount; index++)
                {
                    float4 segment = _ShapeSegments[index], curve = _ShapeCurves[index];
                    if (curve.z > .5)
                    {
                        distance = min(distance, ArcDistance(p - segment.xy, segment.zw, curve.x, curve.y).x);
                        float y = (p.y - segment.y) / segment.w;
                        if (abs(y) < 1)
                        {
                            float root = asin(y);
                            if (ArcCrossing(p, segment, curve.xy, root)) inside = !inside;
                            if (ArcCrossing(p, segment, curve.xy, UNITY_PI - root)) inside = !inside;
                        }
                    }
                    else
                    {
                        float2 a = segment.xy, b = segment.zw, edge = b - a;
                        float2 nearest = a + edge * saturate(dot(p - a, edge) / max(dot(edge, edge), 1e-12));
                        distance = min(distance, length(p - nearest));
                        if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) * edge.x / edge.y) inside = !inside;
                    }
                }
                return distance * (inside ? -1 : 1);
            }
            float OpenArcDistance(float2 p, float2 halfSize)
            {
                if (_ShapeAngles.y < 1e-5 || _ShapeArcThickness <= 0) return 1e10;
                float2 nearest = ArcDistance(p, halfSize, _ShapeAngles.x, _ShapeAngles.y);
                float angle = nearest.y, distance = nearest.x;
                float radius = _ShapeArcThickness * .5;
                if (_ShapeLineCap == 1 || _ShapeAngles.y >= UNITY_PI * 2 - 1e-5) return distance - radius;
                bool start = abs(AngleProgress(angle, _ShapeAngles.x, _ShapeAngles.y)) < 1e-5;
                bool end = abs(AngleProgress(angle, _ShapeAngles.x, _ShapeAngles.y) - _ShapeAngles.y) < 1e-5;
                if (start || end)
                {
                    float2 endpoint = halfSize * float2(cos(angle), sin(angle));
                    float2 tangent = normalize(halfSize * float2(-sin(angle), cos(angle))) * (start ? -1 : 1);
                    float2 delta = p - endpoint;
                    float along = dot(delta, tangent);
                    if (along > 0)
                    {
                        float2 q = float2(along - (_ShapeLineCap == 2 ? radius : 0), abs(dot(delta, float2(-tangent.y, tangent.x))) - radius);
                        return length(max(q, 0)) + min(max(q.x, q.y), 0);
                    }
                }
                return distance - radius;
            }
            float Coverage(float distance, float aa)
            {
                if (_ShapeEdgeMode == 1) return distance <= 0 ? 1 : 0;
                return _ShapeFeather.x > 0 ? FeatherCoverage(distance, aa) : saturate(.5 - distance / aa);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                if (any(abs(_ShapeScale) < 1e-6)) return 0;
                float2 sampleUV = _ShapeEdgeMode == 1 ? (floor(input.uv * _CanvasSize) + .5) / _CanvasSize : input.uv;
                float3 q=float3(sampleUV,1);
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
                if (_ShapeKind == 1)
                {
                    float k = length(p / halfSize);
                    float gradient = length(p / (halfSize * halfSize));
                    distance = k < 1e-6 ? -min(halfSize.x, halfSize.y) : (k - 1) * k / max(gradient, 1e-8);
                    if (_ShapeFeather.x > 0 || _ShapeStyle.y > 0) distance = EllipseDistance(p, halfSize);
                }
                else if (_ShapeKind == 4)
                {
                    if (_ShapeLineCap == 1) distance = length(float2(max(abs(p.x) - halfSize.x, 0), p.y)) - halfSize.y;
                    else distance = BoxDistance(p, float2(halfSize.x + (_ShapeLineCap == 2 ? halfSize.y : 0), halfSize.y));
                }
                else if (_ShapeKind == 5) distance = OpenArcDistance(p, halfSize);
                else distance = ContourDistance(p);
                float aa = max(fwidth(distance), 1e-4);
                float fill = Coverage(distance, aa);
                float outsideWidth = _ShapeStyle.w == 0 ? 0 : _ShapeStyle.w == 1 ? _ShapeStyle.z * .5 : _ShapeStyle.z;
                float insideWidth = _ShapeStyle.z - outsideWidth;
                float strokeCoverage = min(Coverage(distance - outsideWidth, aa), Coverage(-distance - insideWidth, aa)) * _ShapeStyle.y;
                if (_ShapeStyle.z <= 0) strokeCoverage = 0;
                float fillCoverage = fill * _ShapeStyle.x;
                // Replace only the overlapping fill area; an outside stroke must not cut into the fill.
                if (_ShapeStyle.y > 0)
                {
                    float occupied = min(fill, strokeCoverage);
                    fillCoverage = max(0, fillCoverage - occupied * _ShapeStyle.x);
                }
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
