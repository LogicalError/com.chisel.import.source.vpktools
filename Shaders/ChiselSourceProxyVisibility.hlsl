#ifndef CHISEL_SOURCE_PROXY_VISIBILITY_INCLUDED
#define CHISEL_SOURCE_PROXY_VISIBILITY_INCLUDED

// How much of a glow's proxy sphere is in view, from 0 to 1: Source's PixelVisibility_FractionVisible, which the glow
// effects fade by (GlowProxySize). Source draws them on top of everything ($ignorez) and gets away with it because each
// runs an occlusion query against a small sphere around itself; drawing on top without that is exactly what makes a glow
// show through walls. A vertex stage samples the camera depth texture across the sphere's footprint on screen instead.
// URP makes that texture when the pipeline asset asks for it (Depth Texture), and always for the scene view.
//
// Include after URP's Core.hlsl and DeclareDepthTexture.hlsl.

// The centre and two rings across the proxy sphere's footprint, in units of its radius.
static const float2 kProxyTaps[9] =
{
	float2( 0.0,   0.0),
	float2( 0.7,   0.0),  float2( 0.0,   0.7),  float2(-0.7,   0.0),  float2( 0.0,  -0.7),
	float2( 0.35,  0.35), float2(-0.35,  0.35), float2(-0.35, -0.35), float2( 0.35, -0.35)
};

// Source's RemapValClamped, including what it does with an empty range.
float RemapValClamped(float value, float a, float b, float c, float d)
{
	if (a == b)
		return (value >= b) ? d : c;
	return c + (d - c) * saturate((value - a) / (b - a));
}

// How much of the sphere of the given radius around originView is in front of the scene, from 0 to 1
float ProxyVisibility(float3 originView, float proxyRadius)
{
	float eyeDepth = -originView.z;
	if (eyeDepth <= proxyRadius)
		return 1;

	// Screen uv the way URP's own passes read the depth texture with (VertexPositionInputs.positionNDC)
	float4 originCS = TransformWViewToHClip(originView);
	float4 ndc = originCS * 0.5;
	float2 centre = (float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w) / originCS.w;

	bool perspective = IsPerspectiveProjection();
	float2 radius = 0.5 * (perspective ? proxyRadius / eyeDepth : proxyRadius) *
					float2(UNITY_MATRIX_P[0][0], UNITY_MATRIX_P[1][1]);
	float sphereFront = eyeDepth - proxyRadius;

	float visible = 0;
	float tested = 0;
	for (int i = 0; i < 9; i++)
	{
		float2 uv = centre + kProxyTaps[i] * radius;
		// Only what is on screen can be tested, so off-screen taps are left out of the count.
		float onScreen = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
		// SampleSceneDepth loads the texel rather than filtering it, which a vertex stage can do.
		float rawDepth = SampleSceneDepth(saturate(uv));
		float sceneDepth = perspective ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
		visible += onScreen * step(sphereFront, sceneDepth);
		tested += onScreen;
	}
	return (tested > 0) ? (visible / tested) : 1;
}

#endif // CHISEL_SOURCE_PROXY_VISIBILITY_INCLUDED
