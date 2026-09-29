Shader "Hidden/TextureCompositor/ScreenedSeamless"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _Source, _X, _Other, _Direction, _Z;
        sampler2D _RZ, _OldRZ, _PAP, _InitialNorm, _Norm;
        float4 _Size, _FftSize, _ReduceSize, _Axes;
        float _Lambda;
        int _Axis, _Pair, _Multiply;
        float2 Coord(float2 p) { return (p+.5)*_Size.zw; }
        float2 Wrap(float2 p)
        {
            // Only immediate neighbours are sampled. Division-based modulo can
            // round N/N below one on non-power-of-two axes and clamp the seam.
            p.x=_Axes.x==0 ? clamp(p.x,0,_Size.x-1) :
                (p.x<0?p.x+_Size.x:(p.x>=_Size.x?p.x-_Size.x:p.x));
            p.y=_Axes.y==0 ? clamp(p.y,0,_Size.y-1) :
                (p.y<0?p.y+_Size.y:(p.y>=_Size.y?p.y-_Size.y:p.y));
            return p;
        }
        float4 At(float2 p) { return tex2D(_MainTex,Coord(Wrap(p))); }
        float4 U(float2 p) { return tex2D(_Source,Coord(Wrap(p))); }
        float4 Delta(float2 p) { return U(p)-tex2D(_X,Coord(Wrap(p))); }
        float4 Premultiply(v2f_img i):SV_Target
        {
            float4 c=tex2D(_MainTex,i.uv); c.a=saturate(c.a);
            return float4(c.rgb*c.a,c.a);
        }
        float4 Project(v2f_img i):SV_Target
        {
            float2 p=floor(i.uv*_Size.xy);
            if ((_Axis==0 ? _Axes.x : _Axes.y)==0) return At(p);
            int n=_Axis==0?(int)_Size.x:(int)_Size.y;
            int k=_Axis==0?(int)p.x:(int)p.y;
            if(n==1) return At(p);
            if(n<4)
            {
                float4 sum=0;
                [loop] for(int j=0;j<n;j++) { float2 q=p; if(_Axis==0)q.x=j;else q.y=j; sum+=At(q); }
                return sum/n;
            }
            if(k>=2 && k<n-2) return At(p);
            int row=k>=n-2?k-(n-2):k+2;
            float4 value=0;
            [unroll] for(int t=0;t<4;t++)
            {
                float2 q=p; int index=t<2?n-2+t:t-2;
                if(_Axis==0)q.x=index;else q.y=index;
                value+=At(q)*(.25+(row-1.5)*(t-1.5)/5);
            }
            return value;
        }
        float4 Apply(v2f_img i):SV_Target
        {
            float2 p=floor(i.uv*_Size.xy);
            float4 c=At(p);
            return (c-At(p+float2(1,0)))+(c-At(p-float2(1,0)))
                 +(c-At(p+float2(0,1)))+(c-At(p-float2(0,1)))+_Lambda*c;
        }
        float4 Initial(v2f_img i):SV_Target
        {
            float2 p=floor(i.uv*_Size.xy); float4 c=Delta(p);
            float4 r=(c-Delta(p+float2(1,0)))+(c-Delta(p-float2(1,0)))
                    +(c-Delta(p+float2(0,1)))+(c-Delta(p-float2(0,1)))+_Lambda*c;
            if(_Size.x>1 && _Axes.x>0 && (p.x==0 || p.x==_Size.x-1))
            {
                float4 d=.5*((U(float2(1,p.y))-U(float2(0,p.y)))
                             +(U(float2(_Size.x-1,p.y))-U(float2(_Size.x-2,p.y))))
                             -(U(float2(0,p.y))-U(float2(_Size.x-1,p.y)));
                r+=(p.x==0?1:-1)*d;
            }
            if(_Size.y>1 && _Axes.y>0 && (p.y==0 || p.y==_Size.y-1))
            {
                float4 d=.5*((U(float2(p.x,1))-U(float2(p.x,0)))
                             +(U(float2(p.x,_Size.y-1))-U(float2(p.x,_Size.y-2))))
                             -(U(float2(p.x,0))-U(float2(p.x,_Size.y-1)));
                r+=(p.y==0?1:-1)*d;
            }
            return r;
        }
        float4 Pair(v2f_img i):SV_Target
        {
            float2 p=floor(i.uv*_FftSize.xy);
            p=min(p,2*_Size.xy-1-p);
            float4 c=tex2D(_MainTex,Coord(p));
            return _Pair==0?float4(c.r,0,c.g,0):float4(c.b,0,c.a,0);
        }
        float4 Solve(v2f_img i):SV_Target
        {
            float2 k=floor(i.uv*_FftSize.xy);
            float2 s=sin(3.14159265359*min(k,_FftSize.xy-k)*_FftSize.zw);
            return tex2D(_MainTex,i.uv)/(4*dot(s,s)+_Lambda);
        }
        float4 PackRG(v2f_img i):SV_Target { float4 c=tex2D(_MainTex,i.uv*_Size.xy*_FftSize.zw);return float4(c.x,c.z,0,0); }
        float4 PackBA(v2f_img i):SV_Target { float4 c=tex2D(_MainTex,i.uv*_Size.xy*_FftSize.zw);return float4(tex2D(_Other,i.uv).rg,c.x,c.z); }
        float4 Reduce(v2f_img i):SV_Target
        {
            float2 count=ceil(_ReduceSize.xy/4);
            float2 start=floor(i.uv*count)*4;
            float4 sum=0;
            [unroll] for(int y=0;y<4;y++) [unroll] for(int x=0;x<4;x++)
            {
                float2 p=start+float2(x,y);
                if(p.x<_ReduceSize.x && p.y<_ReduceSize.y)
                {
                    float2 uv=(p+.5)*_ReduceSize.zw;
                    float4 v=tex2D(_MainTex,uv);
                    if(_Multiply!=0) v*=tex2D(_Other,uv);
                    sum+=v;
                }
            }
            return sum;
        }
        float4 Step()
        {
            float4 norm=tex2D(_Norm,float2(.5,.5));
            float4 initial=tex2D(_InitialNorm,float2(.5,.5));
            float4 rz=tex2D(_RZ,float2(.5,.5));
            float4 pap=tex2D(_PAP,float2(.5,.5));
            float4 step=0;
            [unroll] for(int c=0;c<4;c++)
                if(norm[c]>max(initial[c]*1e-12,1e-24) && rz[c]>0 && pap[c]>0)
                    step[c]=rz[c]/pap[c];
            return step;
        }
        float4 UpdateX(v2f_img i):SV_Target { return tex2D(_MainTex,i.uv)+Step()*tex2D(_Direction,i.uv); }
        float4 UpdateR(v2f_img i):SV_Target { return tex2D(_MainTex,i.uv)-Step()*tex2D(_Direction,i.uv); }
        float4 UpdateP(v2f_img i):SV_Target
        {
            float4 norm=tex2D(_Norm,float2(.5,.5)), initial=tex2D(_InitialNorm,float2(.5,.5));
            float4 old=tex2D(_OldRZ,float2(.5,.5)), current=tex2D(_RZ,float2(.5,.5));
            float4 z=tex2D(_Z,i.uv), direction=tex2D(_MainTex,i.uv), result=0;
            [unroll] for(int c=0;c<4;c++)
                if(norm[c]>max(initial[c]*1e-12,1e-24) && old[c]>0)
                    result[c]=z[c]+max(current[c],0)/old[c]*direction[c];
            return result;
        }
        float4 Finish(v2f_img i):SV_Target
        {
            float4 c=tex2D(_MainTex,i.uv);
            return float4(c.a>.000001?c.rgb/c.a:0,saturate(c.a));
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Premultiply
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Project
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Apply
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Initial
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Pair
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Solve
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment PackRG
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Reduce
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment UpdateX
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment UpdateR
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment UpdateP
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Finish
            ENDCG }
        Pass {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment PackBA
            ENDCG }
    }
}
