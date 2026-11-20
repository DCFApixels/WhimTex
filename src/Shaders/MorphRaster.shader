Shader "Hidden/WhimTex/MorphRaster"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source", 2D) = "black" {}
        [HideInInspector] _ToTex ("Destination", 2D) = "black" {}
        [HideInInspector] _Flow ("Dense motion", 2D) = "black" {}
        [HideInInspector] _AnchorTable ("UV pairs", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"

        Texture2D<float4> _MainTex, _ToTex, _Flow, _AnchorTable;
        SamplerState sampler_linear_clamp;
        float2 _RasterSize, _SampleGrid;
        float4 _Level;
        int _Coarsest, _CheckStationary;

        // RG supplies motion, B is reserved for visibility. Alpha also carries
        // stationary confidence above the coverage flag and guide influence below it.
        float StationaryConfidence(float packed)
        {
            return saturate(4 * (packed - step(.5, packed)));
        }
        float _RefineSpacing;
        Texture2D<float4> _GuideResiduals;
        Texture2D<float4> _ArcPath, _ArcEquation;
        int _Curved, _LocalPath, _UseVisibility;
        float _MorphTime;
        int _RefineTrace;
        int _AnchorCount, _AnchorWidth, _SegmentCount, _SegmentOffset;

        int _PointRadiusOffset;
        float PointRadius(int index)
        {
            if (_PointRadiusOffset == 0) return 0;
            uint slot = (uint)(_PointRadiusOffset + index), width = (uint)_AnchorWidth;
            return _AnchorTable.Load(int3(slot % width, slot / width, 0)).x;
        }

        float4 GuideOptions(int index)
        {
            uint slot = (uint)(_SegmentOffset + 3 * index + 2), width = (uint)_AnchorWidth;
            return _AnchorTable.Load(int3(slot % width, slot / width, 0));
        }

        // A segment pairs positions at equal fractions along the two directed edges.
        float ProjectSegment(int index, float2 pixel, out float4 start, out float4 end, out float u)
        {
            uint first = (uint)(_SegmentOffset + 3 * index), width = (uint)_AnchorWidth;
            start = _AnchorTable.Load(int3(first % width, first / width, 0));
            uint second = first + 1;
            end = _AnchorTable.Load(int3(second % width, second / width, 0));
            float2 origin = .5 * (start.xy + start.zw) * _Level.xy;
            float2 axis = .5 * (end.xy + end.zw - start.xy - start.zw) * _Level.xy;
            float lengthSq = dot(axis, axis);
            u = saturate(dot(pixel - origin, axis) / max(lengthSq, 1e-8));
            float2 delta = pixel - (origin + u * axis);
            // Opposing endpoints may collapse to one location in the halfway domain.
            return lengthSq > 1e-8 ? dot(delta, delta) : 1e10;
        }

        float ProjectGuide(inout int index, float2 pixel, out float4 a, out float4 b, out float t)
        {
            int limit = index + max(1, (int)GuideOptions(index).x);
            float nearest = ProjectSegment(index++, pixel, a, b, t);
            [loop] while (index < limit)
            {
                float4 start, end;
                float u;
                float distanceSq = ProjectSegment(index++, pixel, start, end, u);
                if (distanceSq < nearest) { nearest = distanceSq; a = start; b = end; t = u; }
            }
            return nearest;
        }

        struct Varyings { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings Vert(appdata_img vertex)
        {
            Varyings o;
            o.pos = UnityObjectToClipPos(vertex.vertex);
            o.uv = vertex.texcoord;
            return o;
        }

        int2 RasterPixel(float2 uv)
        {
            return clamp((int2)(uv * _RasterSize), 0, (int2)_RasterSize - 1);
        }

        float4 PremultipliedTexel(Texture2D<float4> source, int2 pixel)
        {
            if (any(pixel < 0) || any(pixel >= (int2)_RasterSize)) return 0;
            float4 color = source.Load(int3(pixel, 0));
            return float4(color.rgb * color.a, color.a);
        }

        float4 SamplePremultiplied(Texture2D<float4> source, float2 pixel)
        {
            int2 p = (int2)floor(pixel);
            float2 f = frac(pixel);
            float4 first = PremultipliedTexel(source, p);
            if (all(f == 0)) return first;
            return lerp(lerp(first, PremultipliedTexel(source, p + int2(1, 0)), f.x),
                        lerp(PremultipliedTexel(source, p + int2(0, 1)), PremultipliedTexel(source, p + 1), f.x), f.y);
        }

        float2 Signal(float4 c)
        {
            // Bounded matching signal only. The actual warp still samples the original HDR colors.
            float3 rgb = c.rgb / (1.0 + abs(c.rgb));
            float luminance = dot(rgb, float3(.299, .587, .114));
            return c.a * float2(max(rgb.r, max(rgb.g, rgb.b)), luminance);
        }

        float2 VisibleSignal(Texture2D<float4> source, float2 uv)
        {
            float4 c = SamplePremultiplied(source, clamp(uv * _RasterSize - .5, 0, _RasterSize - 1));
            if (c.a > 1e-6) c.rgb /= c.a;
            return Signal(c);
        }

        // A final differential update uses the original images at each output pixel. Solve the
        // 2x2 least-squares system using local gradients, allowing a constant brightness offset.
        float2 RefineMotion(float2 uv, float2 motion)
        {
            float2 stepUV = _RefineSpacing / _RasterSize;
            float2 from = uv - _MorphTime * motion;
            float2 to = uv + (1 - _MorphTime) * motion;
            float values[9], differences[9];
            [unroll] for (int row = 0; row < 3; row++)
            {
                [unroll] for (int col = 0; col < 3; col++)
                {
                    float2 offset = float2(col - 1, row - 1) * stepUV;
                    float a = dot(VisibleSignal(_MainTex, from + offset), float2(.5, .5));
                    float b = dot(VisibleSignal(_ToTex, to + offset), float2(.5, .5));
                    values[row * 3 + col] = lerp(b, a, _MorphTime);
                    differences[row * 3 + col] = a - b;
                }
            }
            float3 sum = 0, tensor = 0;
            float2 rhs = 0;
            [unroll] for (int y = 0; y < 2; y++)
            {
                [unroll] for (int x = 0; x < 2; x++)
                {
                    int i = y * 3 + x;
                    float2 g = .5 * float2(values[i+1] - values[i] + values[i+4] - values[i+3],
                        values[i+3] - values[i] + values[i+4] - values[i+1]);
                    float error = .25 * (differences[i] + differences[i+1] + differences[i+3] + differences[i+4]);
                    sum += float3(g, error);
                    tensor += float3(g.x*g.x, g.x*g.y, g.y*g.y);
                    rhs += g * error;
                }
            }
            tensor -= .25 * float3(sum.x*sum.x, sum.x*sum.y, sum.y*sum.y);
            rhs -= .25 * sum.xy * sum.z;
            tensor.xz += .0001;
            float det = max(1e-8, tensor.x * tensor.z - tensor.y * tensor.y);
            float2 step = float2(tensor.z*rhs.x - tensor.y*rhs.y, tensor.x*rhs.y - tensor.y*rhs.x) / det;
            return motion + clamp(step, -1, 1) * stepUV;
        }

        // Motion is stored in normalized units on the halfway image. Backtrace to that
        // frame for other times, so a moving edge does not stay pinned to its midpoint.
        float3 MotionAt(float2 uv)
        {
            float3 motion = _Flow.SampleLevel(sampler_linear_clamp, uv, 0).xyz;
            float2 previous = motion.xy;
            [unroll] for (int iteration = 0; iteration < 2; iteration++)
            {
                previous = motion.xy;
                motion = _Flow.SampleLevel(sampler_linear_clamp, uv + (.5 - _MorphTime) * motion.xy, 0).xyz;
            }
            // Rotated features can be far from their halfway location. Only
            // continue tracing where the last two estimates still disagree.
            if (_RefineTrace != 0)
            {
                float2 d = motion.xy;
                float2 residual = (d - previous) * _RasterSize;
                float best = dot(residual, residual);
                [loop] for (int i = 0; i < 4 && best > .0625; i++)
                {
                    float2 next = _Flow.SampleLevel(sampler_linear_clamp, uv + (.5 - _MorphTime) * d, 0).xy;
                    residual = (next - d) * _RasterSize;
                    float error = dot(residual, residual);
                    if (error < best)
                    {
                        best = error;
                        motion.xy = next;
                        d = next;
                    }
                    else d = .5 * (d + next);
                }
            }
            [branch] if (_RefineSpacing > 0 && motion.z > .05) motion.xy = RefineMotion(uv, motion.xy);
            return motion;
        }

        float4 GuideResidual(Varyings input) : SV_Target
        {
            int2 index = (int2)input.pos.xy;
            if (index.y * _AnchorWidth + index.x >= _AnchorCount) return 0;
            float4 pair = _AnchorTable.Load(int3(index, 0));
            float2 residual = pair.zw - pair.xy - MotionAt(lerp(pair.xy, pair.zw, _MorphTime)).xy;
            return float4(residual, 0, 0);
        }

        float2 Displacement(float2 pixel)
        {
            float2 uv = (pixel + .5) / _RasterSize;
            float3 motion = MotionAt(uv);
            if (_AnchorCount == 0) return motion.xy * _RasterSize;

            float2 sum = 0, coincident = 0;
            float weightSum = 0;
            int matches = 0;
            float radius = max(4.0, min(_RasterSize.x, _RasterSize.y) * .24);
            [loop] for (int i = 0; i < _AnchorCount; i++)
            {
                uint2 cell = uint2((uint)i % (uint)_AnchorWidth, (uint)i / (uint)_AnchorWidth);
                float4 pair = _AnchorTable.Load(int3(cell, 0));
                float2 delta = (pair.zw - pair.xy) * _RasterSize;
                float2 center = lerp(pair.xy, pair.zw, _MorphTime);
                float2 offset = (uv - center) * _RasterSize;
                float distanceSq = dot(offset, offset);
                if (distanceSq < 1e-7)
                {
                    coincident += delta;
                    matches++;
                }
                else
                {
                    float2 residual = _GuideResiduals.Load(int3(cell, 0)).xy * _RasterSize;
                    float weight = rcp(distanceSq);
                    // Local corrections decay around reliable image matches. In a featureless
                    // area retain the manual deformation supported by the surrounding pins.
                    float influence = lerp(1.0, exp2(-distanceSq / (radius * radius)), motion.z);
                    sum += weight * influence * residual;
                    weightSum += weight;
                }
            }
            if (matches > 0) return coincident / matches;
            return motion.xy * _RasterSize + sum / max(weightSum, 1e-20);
        }

        // Invert the quadratic path on its reference grid. Keep the best residual
        // during damping so a steep local rotation cannot amplify the last step.
        float4 ArcAt(float2 uv, float arcWeight)
        {
            float2 p = uv;
            float4 bestPath = 0;
            float best = 1e30;
            [loop] for (int i = 0; i < 10; i++)
            {
                float4 path = _ArcPath.SampleLevel(sampler_linear_clamp, p, 0);
                float2 residual = uv - (p + (_MorphTime - .5) * path.xy + arcWeight * path.zw);
                float2 pixels = residual * _RasterSize;
                float error = dot(pixels, pixels);
                if (error < best) { best = error; bestPath = path; }
                if (error < .0025) break;
                p += residual * (i == 0 ? 1 : .65);
            }
            return bestPath;
        }

        float EndpointDifference(float4 a, float4 b)
        {
            float4 d = abs(a - b) / (1 + max(abs(a), abs(b)));
            return max(max(d.x,d.y), max(d.z,d.w));
        }

        // Two motions can meet at an occluding edge. Keep a background patch
        // at its original position only if it is visible in both keys, and
        // both warped samples also belong to persistent appearance there.
        float4 ResolveStationaryBackground(int2 p, float2 pa, float2 pb, float4 a, float4 b, float4 mixed, float strength)
        {
            if (strength <= 0) return mixed;
            float4 a0 = PremultipliedTexel(_MainTex, p), b0 = PremultipliedTexel(_ToTex, p);
            float4 fixedColor = lerp(a0, b0, _MorphTime);
            float mismatch = EndpointDifference(a0, b0);
            if (mismatch >= .02 || all(mixed == fixedColor)) return mixed;
            mismatch = max(mismatch, EndpointDifference(a, SamplePremultiplied(_ToTex, pa)));
            mismatch = max(mismatch, EndpointDifference(b, SamplePremultiplied(_MainTex, pb)));
            if (mismatch >= .02) return mixed;
            const int2 offsets[4] = { int2(-2,0), int2(2,0), int2(0,-2), int2(0,2) };
            [unroll] for (int i = 0; i < 4; ++i)
            {
                int2 q = p + offsets[i];
                mismatch = max(mismatch, EndpointDifference(PremultipliedTexel(_MainTex, q), PremultipliedTexel(_ToTex, q)));
            }
            float weight = (1 - smoothstep(.003, .02, mismatch)) * strength;
            return lerp(mixed, fixedColor, weight);
        }

        float4 Warp(Varyings input) : SV_Target
        {
            float2 pixel = RasterPixel(input.uv);
            float2 delta;
            [branch] if (_Curved != 0)
            {
                float weight = 4 * _MorphTime * (1 - _MorphTime);
                float4 path = ArcAt((pixel + .5) / _RasterSize, weight);
                pixel -= weight * path.zw * _RasterSize;
                delta = _LocalPath != 0 ? Displacement(pixel) : path.xy * _RasterSize;
            }
            else delta = Displacement(pixel);
            float2 pa = pixel - _MorphTime * delta, pb = pixel + (1 - _MorphTime) * delta;
            float2 halfway = (pixel + .5 + (.5 - _MorphTime) * delta) / _RasterSize;
            // Opaque photographs cover the canvas at intermediate times too.
            if (_Flow.Load(int3(0, 0, 0)).w > .5)
            {
                pa = clamp(pa, 0, _RasterSize - 1);
                pb = clamp(pb, 0, _RasterSize - 1);
            }
            float4 a = SamplePremultiplied(_MainTex, pa);
            float4 b = SamplePremultiplied(_ToTex, pb);
            float blend = _MorphTime;
            if (_UseVisibility != 0)
            {
                float visibility = _Flow.SampleLevel(sampler_linear_clamp, halfway, 0).z;
                float exponent = 1 + 2 * abs(visibility);
                float wa = pow(1 - blend, exponent);
                float wb = pow(blend, exponent) * exp2(5 * visibility);
                blend = wb / (wa + wb);
            }
            float4 mixed = lerp(a, b, blend);
            if (_CheckStationary != 0 && dot(delta, delta) > .25)
            {
                float packed = _Flow.SampleLevel(sampler_linear_clamp, halfway, 0).w;
                float guide = saturate(4 * (step(.5, packed) - packed));
                float correction = smoothstep(.25, 2.25, dot(delta, delta)) * (1 - guide);
                mixed = ResolveStationaryBackground(RasterPixel(input.uv), pa, pb, a, b, mixed, correction);
            }
            return float4(mixed.a > 0 ? mixed.rgb / mixed.a : float3(0, 0, 0), mixed.a);
        }

        float4 ArcMotion(Varyings input) : SV_Target
        {
            float2 uv = input.pos.xy * _Level.zw;
            return float4(Displacement(uv * _RasterSize - .5) / _RasterSize, 0, 0);
        }

        // For a neighbour edge, match the half-angle and geometric mean length
        // of its endpoint vectors (Liao et al., section 5). Work in image pixels.
        float2 ArcEdge(float2 uv, float2 halfMotion, int2 offset)
        {
            float2 stepUV = offset * _Level.zw;
            float2 next = .5 * _Flow.SampleLevel(sampler_linear_clamp, uv + stepUV, 0).xy;
            float2 edge = stepUV * _RasterSize, dv = (next - halfMotion) * _RasterSize;
            float2 a = edge - dv, b = edge + dv;
            float la = length(a), lb = length(b);
            float2 bisector = a / max(la, 1e-6) + b / max(lb, 1e-6);
            float magnitude = length(bisector);
            if (magnitude < 1e-4 || min(la, lb) < 1e-5) return 0;
            return bisector * (sqrt(la * lb) / magnitude) - edge;
        }

        float4 ArcEquation(Varyings input) : SV_Target
        {
            int2 p = (int2)input.pos.xy, size = (int2)_Level.xy;
            float2 uv = input.pos.xy * _Level.zw;
            float4 flow = _Flow.SampleLevel(sampler_linear_clamp, uv, 0);
            float2 v = .5 * flow.xy;
            float2 rhs = 0;
            if (p.x > 0) rhs -= ArcEdge(uv, v, int2(-1, 0));
            if (p.y > 0) rhs -= ArcEdge(uv, v, int2(0, -1));
            if (p.x + 1 < size.x) rhs -= ArcEdge(uv, v, int2(1, 0));
            if (p.y + 1 < size.y) rhs -= ArcEdge(uv, v, int2(0, 1));
            float screen = saturate(1 - length(v * _RasterSize));
            if (_CheckStationary != 0) screen += 12 * StationaryConfidence(flow.w);
            return float4(rhs, screen, 0);
        }

        float4 ArcRelax(Varyings input) : SV_Target
        {
            int2 p = (int2)input.pos.xy, last = (int2)_Level.xy - 1;
            float3 equation = _ArcEquation.Load(int3(p, 0)).xyz;
            float2 center = _MainTex.Load(int3(p, 0)).xy;
            float2 neighbours =
                _MainTex.Load(int3(max(p - int2(1, 0), 0), 0)).xy +
                _MainTex.Load(int3(max(p - int2(0, 1), 0), 0)).xy +
                _MainTex.Load(int3(min(p + int2(1, 0), last), 0)).xy +
                _MainTex.Load(int3(min(p + int2(0, 1), last), 0)).xy;
            return float4(lerp(center, (neighbours + equation.xy) / (4 + equation.z), .8), 0, 0);
        }

        float4 CopyArc(Varyings input) : SV_Target
        {
            return _MainTex.SampleLevel(sampler_linear_clamp, input.pos.xy * _Level.zw, 0);
        }

        float4 ArcPack(Varyings input) : SV_Target
        {
            int3 p = int3((int2)input.pos.xy, 0);
            float2 bend = _MainTex.Load(p).xy;
            float2 sum = 0;
            float total = 0;
            [loop] for (int i = 0; i < _SegmentCount;)
            {
                float4 a, b;
                float u;
                float radius = GuideOptions(i).y * min(_Level.x, _Level.y);
                float distanceSq = ProjectGuide(i, input.pos.xy, a, b, u);
                float weight = radius > 0 ? 1 - smoothstep(0, max(1, radius * radius), distanceSq) : 1 - smoothstep(1, 9, distanceSq);
                if (weight <= 0) continue;
                float2 startBend = _MainTex.SampleLevel(sampler_linear_clamp, .5 * (a.xy + a.zw), 0).xy;
                float2 endBend = _MainTex.SampleLevel(sampler_linear_clamp, .5 * (b.xy + b.zw), 0).xy;
                sum += weight * lerp(startBend, endBend, u);
                total += weight;
            }
            if (total > 0) bend = lerp(bend, sum / total, saturate(total));
            float4 field = _Flow.Load(p);
            if (_CheckStationary != 0) bend *= 1 - StationaryConfidence(field.w);
            return float4(field.xy, bend / _RasterSize);
        }

        // One column per endpoint. Each row accumulates alpha-weighted texel centers, mass and occupied count.
        float4 RowMoments(Varyings input) : SV_Target
        {
            int2 cell = (int2)input.pos.xy;
            float4 moments = 0;
            [loop] for (int x = 0; x < (int)_SampleGrid.x; x++)
            {
                int2 p = (int2)((float2(x, cell.y) + .5) * (_RasterSize / _SampleGrid));
                float4 color = (cell.x & 1) == 0 ? _MainTex.Load(int3(p, 0)) : _ToTex.Load(int3(p, 0));
                float alpha = saturate(color.a);
                if (cell.x >= 2) moments.xy += Signal(color);
                else moments += float4(((p + .5) / _RasterSize) * alpha, alpha, alpha > 0 ? 1 : 0);
            }
            return moments;
        }

        float4 EstimateTranslation(Varyings input) : SV_Target
        {
            float4 a = 0, b = 0, means = 0;
            [loop] for (int y = 0; y < (int)_SampleGrid.y; y++)
            {
                a += _MainTex.Load(int3(0, y, 0));
                b += _MainTex.Load(int3(1, y, 0));
                means += float4(_MainTex.Load(int3(2,y,0)).xy, _MainTex.Load(int3(3,y,0)).xy);
            }
            if ((int)input.pos.x == 1) return means / max(float4(a.zz,b.zz), 1e-8);
            // A small image samples every texel. A subsampled image needs at least four hits.
            // Both masks need some transparent background and comparable occupied areas.
            float samples = _SampleGrid.x * _SampleGrid.y;
            float minimumHits = all(_RasterSize == _SampleGrid) ? 1 : 4;
            float smaller = min(a.w, b.w), larger = max(a.w, b.w);
            if ((int)input.pos.x == 2)
            {
                // Scale alpha contrast independently of opacity changes between the endpoints.
                if (smaller < minimumHits || smaller < .75 * larger || min(a.z,b.z) < 1e-6) return 0;
                return float4(a.w / a.z, b.w / b.z, 0, 0);
            }
            if (min(a.z, b.z) >= samples) return float4(0, 0, 0, 1);
            if (smaller < minimumHits || larger >= samples || smaller < .75 * larger) return 0;
            if (min(a.z, b.z) <= 0) return 0;
            return float4((b.xy / b.z - a.xy / a.z) * _RasterSize, 0, 0);
        }

        float4 FeatureAt(float2 uv, float4 means, float2 alphaScale)
        {
            int3 p = int3(RasterPixel(uv), 0);
            float4 a = _MainTex.Load(p), b = _ToTex.Load(p);
            // Uncovered pixels have no visible color. A uniformly painted moving silhouette
            // already has its own alpha-based translation and supplies no interior contrast.
            float4 detail = float4(Signal(a), Signal(b)) - means * float4(a.aa, b.aa);
            detail.yw += .7 * saturate(float2(a.a, b.a) * alphaScale);
            return detail;
        }

        float4 Features(Varyings input) : SV_Target
        {
            float2 uv = input.pos.xy * _Level.zw;
            float2 offset = .25 * _Level.zw;
            float4 means = _Flow.Load(int3(1, 0, 0));
            float2 alphaScale = _Flow.Load(int3(2, 0, 0)).xy;
            return .25 * (FeatureAt(uv - offset, means, alphaScale) + FeatureAt(uv + offset, means, alphaScale)
                + FeatureAt(uv + float2(offset.x, -offset.y), means, alphaScale) + FeatureAt(uv + float2(-offset.x, offset.y), means, alphaScale));
        }

        float4 Reduce(Varyings input) : SV_Target
        {
            float2 uv = input.pos.xy * _Level.zw;
            float2 offset = .25 * _Level.zw;
            return .25 * (_MainTex.SampleLevel(sampler_linear_clamp, uv - offset, 0)
                + _MainTex.SampleLevel(sampler_linear_clamp, uv + offset, 0)
                + _MainTex.SampleLevel(sampler_linear_clamp, uv + float2(offset.x, -offset.y), 0)
                + _MainTex.SampleLevel(sampler_linear_clamp, uv + float2(-offset.x, offset.y), 0));
        }

        // Compare centered patches on either side of the halfway frame. Flat pairs have
        // zero evidence. Local normalization tolerates changes of contrast and brightness.
        float PatchScore(float2 uv, float2 delta)
        {
            float2 sumA = 0, sumB = 0;
            float aa = 0, bb = 0, ab = 0;
            [unroll] for (int y = -1; y <= 1; y++)
            {
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 p = uv + float2(x, y) * _Level.zw;
                    float2 a = _MainTex.SampleLevel(sampler_linear_clamp, p - .5 * delta, 0).xy;
                    float2 b = _MainTex.SampleLevel(sampler_linear_clamp, p + .5 * delta, 0).zw;
                    sumA += a; sumB += b;
                    aa += dot(a, a); bb += dot(b, b); ab += dot(a, b);
                }
            }
            aa = max(0, aa - dot(sumA, sumA) / 9.0);
            bb = max(0, bb - dot(sumB, sumB) / 9.0);
            ab -= dot(sumA, sumB) / 9.0;
            float correlation = saturate(ab * rsqrt(max(aa * bb, 1e-12)));
            float evidence = min(aa, bb);
            float2 bias = (sumA - sumB) / 9.0;
            float brightness = rcp(1 + dot(bias, bias) / (.0006 + .035 * (aa + bb)));
            float contrast = 2 * sqrt(aa * bb) / max(aa + bb, 1e-8);
            return correlation * evidence / (evidence + .00012)
                * lerp(.60, 1.0, brightness) * lerp(.65, 1.0, contrast);
        }

        float4 Solve(Varyings input) : SV_Target
        {
            float2 uv = input.pos.xy * _Level.zw;
            float4 previous = _Coarsest != 0 ? _Flow.Load(int3(0,0,0)) : _Flow.SampleLevel(sampler_linear_clamp, uv, 0);
            float3 prior = previous.xyz;
            if (_Coarsest != 0) prior = float3(prior.xy / _RasterSize, 0);
            else
            {
                float2 dx = float2(_Level.z * 2, 0), dy = float2(0, _Level.w * 2);
                float2 left = _Flow.SampleLevel(sampler_linear_clamp, uv - dx, 0).xy;
                float2 right = _Flow.SampleLevel(sampler_linear_clamp, uv + dx, 0).xy;
                float2 up = _Flow.SampleLevel(sampler_linear_clamp, uv - dy, 0).xy;
                float2 down = _Flow.SampleLevel(sampler_linear_clamp, uv + dy, 0).xy;
                // Separable median limits isolated mismatches while retaining coherent motion.
                prior.xy = .5 * (clamp(prior.xy, min(left,right), max(left,right))
                    + clamp(prior.xy, min(up,down), max(up,down)));
            }
            float best = PatchScore(uv, prior.xy);
            float2 result = prior.xy;
            float confidence = best;
            int radius = _Coarsest != 0 ? 3 : 1;
            float2 stepUV = _Level.zw;
            [loop] for (int y = -radius; y <= radius; y++)
            {
                [loop] for (int x = -radius; x <= radius; x++)
                {
                    if (x == 0 && y == 0) continue;
                    float2 d = float2(x, y);
                    float2 candidate = prior.xy + d * stepUV;
                    float score = PatchScore(uv, candidate);
                    float merit = score - .035 - .012 * dot(d, d);
                    if (merit > best && score > .72)
                    {
                        result = candidate;
                        best = merit;
                        confidence = score;
                    }
                }
            }
            return float4(result, max(prior.z, smoothstep(.6, .92, confidence)), previous.w);
        }

        #include "MorphRasterLiao.hlsl"
        #include "MorphRasterDetails.hlsl"
        #include "MorphRasterVisibility.hlsl"
        #include "MorphRasterRigidity.hlsl"

        float4 CopyEndpoint(Varyings input) : SV_Target
        {
            return _MainTex.Load(int3(RasterPixel(input.uv), 0));
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Warp
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RowMoments
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment EstimateTranslation
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment CopyEndpoint
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Features
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Reduce
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Solve
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment GuideResidual
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoInitialize
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoConstraints
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoWarp
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoCoefficients
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoForce
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoRelax
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoSeedScore
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoBestSeed
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment ArcMotion
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment ArcEquation
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment ArcRelax
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment ArcPack
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment CopyArc
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RotationScore
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RotationBest
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RotationPrior
            ENDCG
        }
        Pass
        {
            Name "RotationConsensus"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RotationConsensus
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment VisibilityEvidence
            ENDCG
        }
        Pass
        {
            Name "VisibilityMask"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment VisibilityMask
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment PackVisibility
            ENDCG
        }
        Pass
        {
            Name "BoundaryLinks"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoBoundaryLinks
            ENDCG
        }
        Pass
        {
            Name "RigidGuideModels"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RigidGuideModels
            ENDCG
        }
        Pass
        {
            Name "LocalRigidTargets"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RigidTargets
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment RigidForce
            ENDCG
        }


        Pass
        {
            Name "StationaryEvidence"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment LiaoStationaryEvidence
            ENDCG
        }
    }
    Fallback Off
}
