Shader "Unlit/GlassFresnel3D"
{
    Properties
    {
        _Tint ("Tint (RGBA)", Color) = (0.2, 0.95, 1.0, 0.12)
        _Opacity ("Opacity", Range(0,1)) = 1

        _RimColor ("Rim Color", Color) = (0.8, 1.0, 1.0, 1)
        _RimPower ("Rim Power", Range(0.5, 10)) = 4
        _RimStrength ("Rim Strength", Range(0, 5)) = 1.2

        _SpecColor ("Spec Color", Color) = (1,1,1,1)
        _SpecPower ("Spec Power", Range(1, 256)) = 64
        _SpecStrength ("Spec Strength", Range(0, 3)) = 0.6
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint, _RimColor, _SpecColor;
            float _Opacity, _RimPower, _RimStrength, _SpecPower, _SpecStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nW  : TEXCOORD0;
                float3 vW  : TEXCOORD1;
                float3 wP  : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wP = wPos;
                o.nW = UnityObjectToWorldNormal(v.normal);
                o.vW = normalize(_WorldSpaceCameraPos.xyz - wPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.nW);
                float3 v = normalize(i.vW);

                // Fresnel rim
                float ndv = saturate(dot(n, v));
                float rim = pow(1.0 - ndv, _RimPower) * _RimStrength;

                // Cheap unlit spec highlight from a fixed “light” direction
                float3 l = normalize(float3(0.35, 0.85, 0.25));
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), _SpecPower) * _SpecStrength;

                float3 col = _Tint.rgb;
                col += _RimColor.rgb * rim;
                col += _SpecColor.rgb * spec;

                float a = saturate(_Tint.a * _Opacity + rim * 0.06 + spec * 0.04);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
