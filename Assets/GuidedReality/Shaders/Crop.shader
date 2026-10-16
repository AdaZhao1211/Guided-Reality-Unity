Shader "GuidedReality/TransparentCrop" {
 Properties { _UseVertexColor ("Use particle color", Float) = 0 _Color ("Tint", Color) = (1,1,1,1) _MainTex ("Crop", 2D) = "white" {} }
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" }
  Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
  Cull Off ZWrite Off ZTest LEqual
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
   sampler2D _MainTex; fixed4 _Color; float _UseVertexColor;
   v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=lerp(fixed4(1,1,1,1),v.color,_UseVertexColor); return o; }
   fixed4 frag(v2f i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return tex2D(_MainTex,i.uv) * _Color * i.color; }
   ENDCG
  }
 }
}
