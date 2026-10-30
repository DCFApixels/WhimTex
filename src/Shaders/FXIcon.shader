Shader "Hidden/WhimTex/FXIcon"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4x4 _IconProjection;
            struct Input { float4 vertex : POSITION; float4 color : COLOR; };
            struct Output { float4 vertex : SV_POSITION; float4 color : COLOR; };
            Output Vert(Input input)
            {
                Output output;
                output.vertex = mul(_IconProjection, input.vertex);
                output.color = input.color;
                return output;
            }
            float4 Frag(Output input) : SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
