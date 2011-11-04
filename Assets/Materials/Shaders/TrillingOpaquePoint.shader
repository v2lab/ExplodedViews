Shader "Exploded Views/Trilling Opaque Point" {
	Properties { 
		_TurbulenceAmplitude( "Turbulence amplitude", float ) = 0.03
		_TurbulenceFrequency( "Turbulence frequency", float ) = 5.0
		_TurbulenceCurliness( "Turbulence curliness", float ) = 1.0
		
		_NearTint( "Near tint", Color ) = (0.8,1,0.8,0.1)
		
		_TunnelD ("Tunnel Distance", float) = 1.0
		_TunnelRadius("Tunnel Radius", Range(0, 1)) = 0.5
		_TunnelAspect("Tunnel Aspect", float) = 1.33
		
		_SubLod ( "SubLod", Range(0,1) ) = 0
	}
	
	// Levels of detail:
	//   0:	simple (possibly foggy) points
	// 250:	turbulence
	// 500:	turbulence + near-tint fading in + tunnel
	
	SubShader {
	LOD 500
	Tags { "Queue" = "Geometry" }

	Pass {
CGPROGRAM
#pragma target 3.0
#pragma vertex vert
#pragma fragment frag

#include "UnityCG.cginc"
#include "ExplodedShaderLib.cginc"
#include "noise3d.cginc"

uniform float _TurbulenceAmplitude, _TurbulenceCurliness, _TurbulenceFrequency;
uniform float _SubLod;
uniform float4 _NearTint;
uniform float _TunnelD, _TunnelRadius, _TunnelAspect;

PointV2F vert (PointVIn v)
{
	PointV2F o;
	
	v.vertex.xyz += _TurbulenceAmplitude * snoise3( _TurbulenceCurliness * v.vertex.xyz, _TurbulenceFrequency * _Time);
	
	o.pos = mul (UNITY_MATRIX_MVP, v.vertex);
	
	float displacement = max(0.0, 1.0 - o.pos.z / _TunnelD);
	
	o.pos.xy += normalize(o.pos.xy) 
					* displacement * displacement
					* float2(1.0,_TunnelAspect)
					* _TunnelRadius;

    // billboard...
	Billboard( o.pos, v.texcoord.xy, 1.0 );

    // pass the color along...
    o.color = v.color;

 	return o;
}

half4 frag(PointV2F i) : COLOR { return lerp( i.color, _NearTint, _SubLod * _NearTint.a ); }

ENDCG

		}
	}
	

	SubShader {
		LOD 250
		Tags { "Queue" = "Geometry" }

		Pass {
CGPROGRAM
#pragma target 3.0
#pragma vertex vert
#pragma fragment frag

#include "UnityCG.cginc"
#include "ExplodedShaderLib.cginc"
#include "noise3d.cginc"

uniform float _TurbulenceAmplitude, _TurbulenceCurliness, _TurbulenceFrequency;

PointV2F vert (PointVIn v)
{
	PointV2F o;
	
	v.vertex.xyz += _TurbulenceAmplitude * snoise3( _TurbulenceCurliness * v.vertex.xyz, _TurbulenceFrequency * _Time);
	
	o.pos = mul (UNITY_MATRIX_MVP, v.vertex);

    // billboard...
	Billboard( o.pos, v.texcoord.xy, 1.0 );

    // pass the color along...
    o.color = v.color;

 	return o;
}

half4 frag(PointV2F i) : COLOR { return i.color; }

ENDCG

		}
	}

	SubShader {
	LOD 0
	Pass {
CGPROGRAM
#pragma vertex vert
#pragma fragment frag

#include "UnityCG.cginc"
#include "ExplodedShaderLib.cginc"


PointV2F vert (PointVIn v)
{
	PointV2F o;
	o.pos = mul (UNITY_MATRIX_MVP, v.vertex);

    // billboard...
	Billboard( o.pos, v.texcoord.xy, 1.0 );

    // pass the color along...
    o.color = v.color;

 	return o;
}

half4 frag(PointV2F i) : COLOR { 
	return i.color; 
}


ENDCG
		}
	}
}