Shader "Hidden/WhimTex/NormalMap"
{
    Properties { [HideInInspector] _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "HdrColor.cginc"
        sampler2D _MainTex, _Fine, _Medium, _Large, _Input;
        float4 _Pixel, _Levels, _BlurStep, _Details, _Flip;
        float _Channel, _InputSpace, _Edges, _IgnoreTransparent, _Mode, _Strength;
        float _Derivative, _AlphaMode, _Output, _Encoding;
        float2 edgeUv(float2 uv)
        {
            if (_Edges > 1.5) uv = 1.0 - abs(frac(uv * .5) * 2.0 - 1.0);
            else if (_Edges > .5) return frac(uv);
            return clamp(uv, _Pixel.xy * .5, 1.0 - _Pixel.xy * .5);
        }
        float4 extract(v2f_img i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            if (_InputSpace < .5) c.rgb = SpriteEncode(max(c.rgb, 0.0));
            float h = _Channel < .5 ? dot(c.rgb, float3(.2126, .7152, .0722)) :
                _Channel < 1.5 ? c.r : _Channel < 2.5 ? c.g : _Channel < 3.5 ? c.b :
                _Channel < 4.5 ? c.a : max(c.r, max(c.g, c.b));
            h = pow(saturate((h - _Levels.x) / max(.0001, _Levels.y - _Levels.x)), 1.0 / _Levels.z);
            if (_Levels.w > .5) h = 1.0 - h;
            float weight = _IgnoreTransparent > .5 && (_Channel < 3.5 || _Channel > 4.5) ? saturate(c.a) : 1.0;
            return float4(h * weight, weight, 0, 1);
        }
        float4 blur(v2f_img i) : SV_Target
        {
            float2 d = _BlurStep.xy;
            float2 sum = tex2D(_MainTex, edgeUv(i.uv)).rg * .375;
            sum += (tex2D(_MainTex, edgeUv(i.uv - d)).rg + tex2D(_MainTex, edgeUv(i.uv + d)).rg) * .25;
            sum += (tex2D(_MainTex, edgeUv(i.uv - 2.0*d)).rg + tex2D(_MainTex, edgeUv(i.uv + 2.0*d)).rg) * .0625;
            return float4(sum, 0, 1);
        }
        float value(float2 pair, float fallback) { return pair.y > .00001 ? pair.x / pair.y : fallback; }
        float heightAt(float2 uv, float3 fallback)
        {
            uv = edgeUv(uv);
            float fine = value(tex2D(_Fine, uv).rg, fallback.x);
            if (_Mode < .5) return fine;
            float medium = value(tex2D(_Medium, uv).rg, fallback.y);
            float large = value(tex2D(_Large, uv).rg, fallback.z);
            return .5 + (fine-medium)*_Details.x + (medium-large)*_Details.y +
                (large-.5)*_Details.z*(1.0-_Details.w);
        }
        float4 normal(v2f_img i) : SV_Target
        {
            float3 h = value(tex2D(_Fine, edgeUv(i.uv)).rg, .5).xxx;
            if (_Mode > .5)
            {
                h.y = value(tex2D(_Medium, edgeUv(i.uv)).rg, h.x);
                h.z = value(tex2D(_Large, edgeUv(i.uv)).rg, h.x);
            }
            float2 t = _Pixel.xy;
            float l = heightAt(i.uv - float2(t.x,0), h), r = heightAt(i.uv + float2(t.x,0), h);
            float b = heightAt(i.uv - float2(0,t.y), h), u = heightAt(i.uv + float2(0,t.y), h);
            float2 slope = float2(r-l, u-b) * .5;
            if (_Derivative < 1.5)
            {
                float bl = heightAt(i.uv-t,h), tr = heightAt(i.uv+t,h);
                float tl = heightAt(i.uv+float2(-t.x,t.y),h), br = heightAt(i.uv+float2(t.x,-t.y),h);
                float side = _Derivative < .5 ? 1.0 : 3.0;
                float center = _Derivative < .5 ? 2.0 : 10.0;
                slope = float2(side*(br+tr-bl-tl)+center*(r-l), side*(tl+tr-bl-br)+center*(u-b)) / (4.0*side+2.0*center);
            }
            float3 n = normalize(float3(-slope * _Strength * _Flip.xy, 1.0));
            float3 rgb = _Output > .5 ? saturate(heightAt(i.uv,h)).xxx : n * .5 + .5;
            if (_Encoding < .5) rgb = SpriteDecode(rgb);
            return float4(rgb, _AlphaMode > .5 ? saturate(tex2D(_Input,i.uv).a) : 1.0);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment extract
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment blur
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment normal
            ENDCG
        }
    }
    Fallback Off
}
