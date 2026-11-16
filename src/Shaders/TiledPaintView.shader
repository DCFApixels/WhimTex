Shader "Hidden/WhimTex/TiledPaintView"
{
 SubShader { Pass {
  ZTest Always ZWrite Off Cull Off Blend Off
  HLSLPROGRAM
  #pragma target 5.0
  #pragma vertex Vert
  #pragma fragment Frag
  #include "UnityCG.cginc"
  #include "TiledPaintComposite.hlsl"
  float3 _ViewInverse0, _ViewInverse1, _ViewInverse2;
  float4 _SourceSize, _TileKey;
  float _Point, _ClipFrame, _FallbackGamma, _OutputMode;
  Texture2D<float4> _Fallback, _DenseOverride; float _UseDenseOverride;
  float _HasFallback;
  Texture2D<float4> _Base0, _Ink0; float4 _BaseMap0, _InkMap0;
  Texture2D<float4> _Base1, _Ink1; float4 _BaseMap1, _InkMap1;
  Texture2D<float4> _Base2, _Ink2; float4 _BaseMap2, _InkMap2;
  Texture2D<float4> _Base3, _Ink3; float4 _BaseMap3, _InkMap3;
  struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
  V Vert(uint id:SV_VertexID) { V o; o.uv=float2(id==2?2:0,id==1?2:0); o.pos=mul(UNITY_MATRIX_VP,float4(o.uv,0,1)); return o; }
  float4 ReadPixel(int2 pixel)
  {
   if(_ClipFrame>.5) pixel=clamp(pixel,0,(int2)_SourceSize.xy-1);
   int2 key=(int2)floor(pixel/256.0), local=pixel-key*256;
   int2 d=key-(int2)_TileKey.xy;
   int index=d.y*2+d.x;
   float4 color=0, ink=0; float pending=0;
   if(index==0) {
    if(_BaseMap0.z>.5) color=_Base0.Load(int3(local+(int2)_BaseMap0.xy,0));
    pending=_InkMap0.z;
    if(pending>.5) ink=_Ink0.Load(int3(local+(int2)_InkMap0.xy,0));
   }
   else if(index==1) {
    if(_BaseMap1.z>.5) color=_Base1.Load(int3(local+(int2)_BaseMap1.xy,0));
    pending=_InkMap1.z;
    if(pending>.5) ink=_Ink1.Load(int3(local+(int2)_InkMap1.xy,0));
   }
   else if(index==2) {
    if(_BaseMap2.z>.5) color=_Base2.Load(int3(local+(int2)_BaseMap2.xy,0));
    pending=_InkMap2.z;
    if(pending>.5) ink=_Ink2.Load(int3(local+(int2)_InkMap2.xy,0));
   }
   else if(index==3) {
    if(_BaseMap3.z>.5) color=_Base3.Load(int3(local+(int2)_BaseMap3.xy,0));
    pending=_InkMap3.z;
    if(pending>.5) ink=_Ink3.Load(int3(local+(int2)_InkMap3.xy,0));
   }





   if(pending>.5) color=TiledCompositeValue(color,ink,pixel+.5);
   if(_UseDenseOverride>.5 && all(pixel>=0) && all(pixel<(int2)_SourceSize.xy))color=_DenseOverride.Load(int3(pixel,0));
   // Match committed Half storage before interpolation, including pending preview.
   color=f16tof32(f32tof16(color));
   if(color.a>.00001) color.rgb=clamp(color.rgb/color.a,-65504,65504);
   else {
    color.rgb=0;
    if(_HasFallback>.5 && all(pixel>=0) && all(pixel<(int2)_SourceSize.xy)) {
     color.rgb=_Fallback.Load(int3(pixel,0)).rgb;
     if(_FallbackGamma>.5) color.rgb=SpriteDecode(color.rgb);
    }
   }
   return color;
  }
  float SafeStored(float value) { return (asuint(value)&0x7fffffffu)>=0x7f800000u ? 0 : clamp(value,-65504,65504); }
  float4 Output(float4 color) {
   if(_OutputMode>1.5) {
    color=float4(SafeStored(color.r),SafeStored(color.g),SafeStored(color.b),saturate(SafeStored(color.a)));
    if(_OutputMode<2.5) color.rgb=SpriteEncode(saturate(color.rgb));
   }
   return color;
  }
  float4 Frag(V i):SV_Target {
   float3 p=float3(i.uv,1); float w=dot(_ViewInverse2,p); if(w<1e-8) discard;
   float2 uv=float2(dot(_ViewInverse0,p),dot(_ViewInverse1,p))/w;
   if(_ClipFrame>.5 && (any(uv<0)||any(uv>1))) discard;
   float2 pixel=uv*_SourceSize.xy;
   float2 owner=floor(pixel-(_Point>.5?0:.5));
   if(_ClipFrame>.5) owner=clamp(owner,0,_SourceSize.xy-1);
   int2 key=(int2)floor(owner/256.0);
   if(any(key!=(int2)_TileKey.xy)) discard;
   if(_Point>.5) return Output(ReadPixel((int2)floor(pixel)));
   pixel-=.5; int2 at=(int2)floor(pixel); float2 t=frac(pixel);
   return Output(lerp(lerp(ReadPixel(at),ReadPixel(at+int2(1,0)),t.x),lerp(ReadPixel(at+int2(0,1)),ReadPixel(at+1),t.x),t.y));
  }
  ENDHLSL
 } }
}
