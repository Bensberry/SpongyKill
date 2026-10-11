// Outline shell for characters: an enlarged copy of a body part drawn with its front faces culled,
// so only a rim shows around the part. The enlargement is computed per axis so every face grows by
// the same width, and that width is a fixed number of screen pixels at any distance (with a small
// minimum in metres), so enemies stay readable far away without looking bloated up close.
// Fog is deliberately not applied: the outline is meant to cut through the underwater haze.
Shader "Spongykill/CharacterOutline"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.15, 0.12, 1)
        _Width ("Width (pixels)", Float) = 2.5
        _MinWorldWidth ("Minimum width (metres)", Float) = 0.006
        _Extents ("Mesh half size (object space)", Vector) = (0.5, 0.5, 0.5, 0)
        _Center ("Mesh centre (object space)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
                float _MinWorldWidth;
                float4 _Extents;
                float4 _Center;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4x4 objectToWorld = GetObjectToWorldMatrix();
                float3 axisScale = float3(length(objectToWorld._m00_m10_m20), length(objectToWorld._m01_m11_m21), length(objectToWorld._m02_m12_m22));
                float3 centerWS = TransformObjectToWorld(_Center.xyz);
                float distanceToCamera = length(centerWS - GetCameraPositionWS());
                // World size of one screen pixel at that distance.
                float pixelWorld = 2.0 * distanceToCamera / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float width = max(_Width * pixelWorld, _MinWorldWidth);
                float3 halfSizeWS = max(_Extents.xyz * axisScale, 1e-4);
                float3 scale = 1.0 + width / halfSizeWS;
                float3 positionOS = _Center.xyz + (input.positionOS.xyz - _Center.xyz) * scale;
                output.positionCS = TransformObjectToHClip(positionOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
