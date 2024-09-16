using System;
using System.Collections.Generic;
using System.IO;

using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Debug = UnityEngine.Debug;
using System.Globalization;

namespace Chisel.Import.Source.VPKTools
{
	public class VmfMaterial
	{
		public string		MaterialTypeName;

		public RGBColor		Color;

		public int?			BaseTextureFrame;
		public string       BaseTextureName;
		public string       BaseTexture2Name;
		public float?       BaseAlphaEnvMapMask;

		public bool?		BumpSelfShadowing;
		public string       BumpMapName;
		public string       BumpMap2Name;
		public int?			BumpFrame;
		
		public string       NormalMapName;
		public bool?        NormalMapAlphaEnvMapMask;

		public bool?		SelfIllumination;
		public string       SelfIlluminationMask;
		public string       SelfIlluminationTexture;
		public RGBColor		SelfIlluminationColor;
		public float?       SelfIlluminationAlphaEnvMapMask;

		public bool?		Phong;
		public string       PhongWarpTextureName;
		public string		PhongExponentTextureName;	
		public float?		PhongExponentValue;
		public float?		PhongBoost;
		//public Vector3?	PhongFresnelRanges;
		public RGBColor		PhongTint;
		public bool?		PhongAlbedoTint;
		
		public string		DetailTextureName;
		public Vector2?		DetailScale;
		public float?		DetailBlendFactor;

		public enum DetailBlendModeType
		{
			Original								= 0,	// 0 = original mode
			AdditiveBase							= 1,	// 1 = ADDITIVE base.rgb+detail.rgb*fblend
			AlphaBlendOverBase						= 2,	// 2 = alpha blend detail over base
			StraightFade							= 3,	// 3 = straight fade between base and detail.
			BaseAlphaBlend							= 4,	// 4 = use base alpha for blend over detail
			AddDetailColorPostLighting				= 5,	// 5 = add detail color post lighting
			CombineRgbAdditiveSelfIllumThesholdFade	= 6,	// 6 = TCOMBINE_RGB_ADDITIVE_SELFILLUM_THRESHOLD_FADE 6
			BaseAlphaChannelSelect					= 7,	// 7 = use alpha channel of base to select between mod2x channels in r+a of detail
			Multiply								= 8,	// 8 = multiply
			BaseAlphaChannelMaskBase				= 9,	// 9 = use alpha channel of detail to mask base
			SelfShadowBumpModulation				= 10,	// 10 = use detail to modulate lighting as an ssbump
			SelfShadowBumpAlbedo					= 11,	// 11 = detail is an ssbump but use it as an albedo. shader does the magic here - no user needs to specify mode 11
			NoDetailTexture							= 12	// 12 = there is no detail texture
		}
		public DetailBlendModeType? DetailBlendMode;

		public string       EnvMapTextureName;
		public string       EnvMapMaskTextureName;
		public RGBColor		EnvMapTint;
		//public Vector3?	EnvMapContrast;
		public Vector3?		EnvMapSaturation;
		public int?			EnvMapMode;

		public string       BlendModulateTextureName;

		public bool?        Translucent;
		public float?		Additive;		
		public bool?        NoCull;

		public float?       AlphaTestReference;
		public bool?		AlphaTest;


		public bool HaveCutout { get { return AlphaTest.HasValue && AlphaTest.Value; } }
		public bool HaveTranslucency { get { return Translucent.HasValue && Translucent.Value; } }
		public bool HaveAdditiveBlending { get { return Additive.HasValue && (Additive.Value > 0); } }
		public bool HaveTransparency { get { return HaveTranslucency || HaveAdditiveBlending || HaveCutout; } }



		public void GetAllTextureNames(HashSet<string> outputTextureNames)
		{
			if (BaseTextureName != null) outputTextureNames.Add(BaseTextureName);
			if (BaseTexture2Name != null) outputTextureNames.Add(BaseTexture2Name);
			if (BumpMapName != null) outputTextureNames.Add(BumpMapName);
			if (BumpMap2Name != null) outputTextureNames.Add(BumpMap2Name);
			if (NormalMapName != null) outputTextureNames.Add(NormalMapName);

			if (SelfIlluminationMask != null) outputTextureNames.Add(SelfIlluminationMask);
			if (SelfIlluminationTexture != null) outputTextureNames.Add(SelfIlluminationTexture);
			if (PhongWarpTextureName != null) outputTextureNames.Add(PhongWarpTextureName);
			if (PhongExponentTextureName != null) outputTextureNames.Add(PhongExponentTextureName);
			if (DetailTextureName != null) outputTextureNames.Add(DetailTextureName);
			if (BlendModulateTextureName != null) outputTextureNames.Add(BlendModulateTextureName);
			if (EnvMapMaskTextureName != null) outputTextureNames.Add(EnvMapMaskTextureName);
			if (EnvMapTextureName != null) outputTextureNames.Add(EnvMapTextureName);
		}

		public static VmfMaterial Read(Stream stream)
		{
			var vmfString = TextParser.LoadStreamAsString(stream);
			try
			{
				return ParseString(vmfString);
			}
			catch (Exception ex)
			{
				Debug.LogError(ex.Message + $".\n" + TextParser.AddLineNumbers(vmfString));
				return null;
			}
		}

		static VmfMaterial ParseString(string text)
		{
			TextParser materialFile = TextParser.ParseString(text);
			if (materialFile == null)
			{
				Debug.LogError($"materialFile == null");
				return null;
			} 
			if (materialFile.Properties.Length != 1)
			{
				Debug.LogError($"materialFile.Properties.Length != 1");
				return null;
			}


			var vmfMaterial	= new VmfMaterial();
			vmfMaterial.Color = UnityEngine.Color.white;
			vmfMaterial.MaterialTypeName = materialFile.Properties[0].Name;
			var properties	= materialFile.Properties[0].Children;
			if (properties != null &&
				properties.Length > 0)
			{
				for (var i = 0; i < properties.Length; i++)
				{
					var property = properties[i];
					// .. somebody made a mistake with a comment 
					if (property.Name.StartsWith("//") ||
						property.Name.StartsWith("\\\\"))
						continue;
					switch (property.Name.ToLowerInvariant())
					{
						case "shadername":

						case "water_dx60":
						case "water_dx80":
						case "water_dx81":
						case "water_dx90": // ''.
						case "<dx90":
						case ">=dx90": // ''.
						case ">=dx90_20b": // ''.
						case "<dx90_20b": // ''.
						case "gpu<1": // ''.
						case "gpu<2?$blendframes": // '0'.
						case "gpu<2?$depthblend": // '0'.
						case "refract_dx60":
						case "refract_dx80": // ''.
						case "core_dx70": // ''.
						case "core_dx80": // ''.
						case "eyes_dx8": // ''.
						case "lightmappedgeneric_dx6": // ''.
						case "lightmappedgeneric_dx8": // ''.
						case "lightmappedgeneric_dx9": // ''.
						case "lightmappedgeneric_hdr_dx9": // ''.
						case "lightmappedgeneric_nobump_dx8": // ''.
						case "modulate_dx6": // ''.
						case "screenspace_general_dx8": // ''.
						case "unlitgeneric_dx6": // ''.
						case "unlitgeneric_hdr_dx9": // ''.
						case "vertexlitgeneric_dx8": // ''.
						case "vertexlitgeneric_hdr_dx9": // ''.

						case "!360?$allowalphatocoverage":
						case "!srgb_pc?$outputintensity": // '1.7'.
						case "!$flashlightnolambert":
						case "!lowfill?$depthblend": // '1'.
						case "!lowfill?$endfadesize": // '1.1'.
						case "!lowfill?$startfadesize": // '.7'.
						case "!srgb?$overbrightfactor": // '1.2'. '1.5'.

						case "360?$color2": // '[ 0.6 0.6 0.6 ]'.
						case "360?$linearwrite": // '1'.
						case "360?$gammacolorread": // '1'.
						case "360?$basetexture": // 'sprites/light_glow02_360'. 'vgui/zoom_360'.
						case "360?$outputintensity": // '2.8'.
						case "sonyps3?$outputintensity": // '.7'.
						case "srgb_pc?$outputintensity": // '2.3'.
						case "srgb?$basetexture": // 'tools/tools_xogvolume'.
						case "srgb?$color2": // '[1.1 1.1 1.1]'.
						case "srgb?$fogcolor": // '{21 48 52}'.

						case "???$selfillum???": // '1'.
						case "lowfill?$blendframes": // '0'.
						case "lowfill?$depthblend": // '0'.
						case "lowfill?$endfadesize": // '.2'.
						case "lowfill?$maxfadesize": // '.35'.
						case "lowfill?$minfadesize": // '.15'.
						case "lowfill?$startfadesize": // '.1'.

						case "%compile2dsky": // '1'.
						case "%compileblocklos": // '1'.
						case "%compileclip": // '0'. '1'.
						case "%compiledetail": // '0'. '1'. 
						case "%compilefog": // '1'.
						case "%compilehint": // '0'. '1'.
						case "%compileinvisible": // '0'.
						case "%compileladder":
						case "%compilenochop": // '0'. '1'.
						case "%compilenodraw": // '1'. '1'.
						case "%compilenolight": // '0'. '1'.
						case "%compilenonsolid": // '1'. '1'.
						case "%compilenpcclip": // '0'. '1'.
						case "%compileorigin": // '0'. '1'.
						case "%compilepassbullets":
						case "%compileplayercontrolclip": // '0'.
						case "%compileskip": // '0'. '1'.
						case "%compilesky": // '0'. '1'.
						case "%compileslime": // '0'. '1'.
						case "%compiletrigger": // '0'. '1'.
						case "%compilewater": // int 
						case "%compilewet": // '0'. '1'.
						case "%detailtype":
						case "%detailtype2": // 'desert_norock_wave'.
						case "%keywords"://string
						case "%keyworkds": // 'bms'.
						case "%noportal": // '1'.
						case "%noToolTexture": // '1'.
						case "%notooltexture": // '1'.
						case "%playerclip": // '0'. '1'.
						case "%tooltexture":

						case "$pomsilhouettes":
						case "$minu":
						case "$maxu":
						case "$minv":
						case "$maxv":

						case "$2basetexture": // 'voice/speaker4'.
						case "$a_b_halfwidth": // '0.005'. '0.1'.
						case "$a_b_noise": // '0'.
						case "$a_s_halfwidth": // '0.002'. '0.025'.
						case "$a_s_noise": // '0'.
						case "$a_t_halfwidth": // '0.00'.
						case "$a_threshold": // '0.02'. '0.7'.
						case "$aaenable": // '1'.
						case "$aainternal1": // '[0 0 0 0]'.
						case "$aainternal2": // '[0 0 0 0]'.
						case "$aainternal3": // '[0 0 0 0]'.
						case "$abovewater":
						case "$abstmp": // '0'.
						case "$addbasetexture2": // '0.1'.
						case "$addoverblend": // '1'.
						case "$addself": // '1.5'.
						case "$albedo": // ''.
						case "$ambientocclcolor": // '[0.33 0.33 0.33]'.
						case "$ambientoccltexture": // 'models/humans/eye-extra'.
						case "$ambientocclusion": // '1'.
						case "$ambientocclusiontexture": // 'models/alyx/alyx_occlusion'.
						case "$ambientonly": // '0'. '1'.
						case "$angle":
						case "$animatedtextureframenumvar": // '$frame'.
						case "$animatedtextureframerate": // '1.00'.
						case "$animatedtexturevar": // '$basetexture'.
						case "$basealmapalphaenvmapmask": // '1'.
						case "$baseapalphaenvmapmask": // '1'.
						case "$basemapalphaphongmask": // '1'.
						case "$basemapluminancephongmask": // '1'.
						case "$basenormalmap2": // 'nature\bnc_dirt_rocky_large_001_norm'. 'xen/xen_swamp_roots_01a_ssbump'. 'xen/xen_tree_roots_01a_n'. 'xen/xen_wetmud_01a_normal'.
						case "$basetexture3": // 'xen\xen_rockwall_001a'. 'xen/xen_sludge_02a'. 'xen/xen_wetmud_01a_diff'.
						case "$basetexturelow": // '_rt_dofblurdownsampled'.
												//case "$basetexturetransform2": // 'center .5 .5 scale 1 1 rotate 0 translate 0 0'.
												//case "$basetexturetransform2": // 'center .5 .5 scale 1 1 rotate 10 translate 0 0'.
												//case "$basetexturetransform2": // 'center .5 .5 scale 2 2 rotate 0 translate 0 0'.
												//case "$basetexturetransform2": // 'center .5 .5 scale 4 4 rotate 0 translate 0 0'.
												//case "$basetexturetransform2": // 'center .5 .5 scale 8 8 rotate 0 translate 0 0'.
												//case "$basetexturetransform2": // 'center 0 0 scale 0.005 0.005 rotate 0 translate 0 0'.
						case "$basetexturetransform2": // 'scale .1 .1'.
						case "$basettexture": // 'i dont exist'.
						case "$blendframes": // '0'. '1'.
						case "$blendmodulate": // 'xen/blend/xen_modulate_001'.
						case "$blendtintbybasealpha": // '1'. 
						case "$blendtintcoloroverbase": // '1.0'.
						case "$blinkmulti": // '0'.
						case "$blinkoff": // '0.125'.
						case "$blinkon": // '1.0'.
						case "$blinkrate": // '0'.
						case "$bloomamount": // '1'.
						case "$bloomenable": // '1'.
						case "$bloomexp": // '6'.
						case "$bloomexponent": // '3'.
						case "$bloomsaturation": // '1'.
						case "$bloomscale": // '1'.
											//case "$bloomtexture": // '_rt_fullframefb2'.
											//case "$bloomtexture": // '_rt_small16fb0'.
											//case "$bloomtexture": // '_rt_small2fb0'.
											//case "$bloomtexture": // '_rt_smallfb0'.
											//case "$bloomtexture": // '_rt_smallfb2'.
											//case "$bloomtexture": // '_rt_smallhdr0'.
						case "$bloomtexture": // '_rt_small8fb0'.
						case "$bloomtintenable": // '1'. '2'.
						case "$bloomtype": // '0'.
						case "$bluramount":
						//case "$blurtexture": // '_rt_smallfb0'.
						case "$blurtexture": // '_rt_smallhdr0'.
						case "$bottommaterial":
						case "$brightness": // 'effects/spark_brightness'.
						case "$bumpamp":
						case "$bumpbasetexture2withbumpmap": // '0'. '1'.
						case "$bumpmap3": // 'models/boxrocket_chell_head/face_1.0_n'.
						case "$bumpmaptransform": // 'center 1.0 1.0 scale 2.0 2.0 rotate 0.0 translate 0.5 0.5'.
						case "$bumpoffset":
						case "$bumpscale": // '0.25'. '0.50'.
						case "$bumpstretch": // 'models/shadertest/shader1_normal'.
											 //case "$bumptransform": // 'center .75 .75 scale 2 2 rotate 0 translate 0 0'.
											 //case "$bumptransform": // 'center 0 0 scale 2 2 rotate 0 translate 0 0'.
											 //case "$bumptransform": // 'center 1 1 scale 2 2 rotate 0 translate .50 0'.
											 //case "$bumptransform": // 'center 1 1 scale 2 2 rotate 0 translate 0 0'.
						case "$bumptransform": // '[ 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 ]'.
						case "$bumpwrinkle": // 'models/shadertest/shader3_normal'.
						case "$burn_grad": // 'dev/vortex_burn_grad_1d'.
						case "$burn_noise": // 'dev/burnableweb3_mask'. 'dev/xognoise'.
						case "$c0_x": // '1'.
						case "$c0_y": // '1'.
						case "$c0_z": // '1'.
						case "$c1_x": // '3'.
						case "$c2_x": // '1'.
						case "$c2_y": // '0'.
						case "$center":
						case "$charge": // '0'.
						case "$chargeint": // '0'.
						case "$chargerate": // '0'.
						case "$chargereset": // '0'.
						case "$chargestart": // '1'.
						case "$cheapwaterenddistance": // '1000.0'. '3000'.
						case "$cheapwaterstartdistance": // '2000'. '500.0'.
						case "$chromatic_cubic_distortion": // '0.00'.
						case "$chromatic_dispertionby": // '0.5'.
						case "$chromatic_dispertionrc": // '0.5'.
						case "$chromatic_enable": // '1'.
						case "$chromatic_lens_distortion": // '0.00'.
						case "$chromatic_scale": // '0.94'.
						case "$chromatic_strength": // '0'.
						case "$clamps": // '1'.
						case "$clampt": // '1'.
						case "$clientshader": // 'mouthshader'.
						case "$cloakfactor": // '0.99'.
						case "$cloakpassenabled": // '1'.
						case "$cloudalphatexture": // 'shadertest/cloudalpha'. 'shadertest/jet_mask'.
						case "$cloudbrightness": // '1.2'. '1.8'. '2'.
						case "$cloudcolour": // '{16 192 255}'. '[ 2.00 2.00 2.00 ]'.
						case "$colcorrect_defaultweight": // '1'.
						case "$colcorrect_lookupweights": // '[0 0 0 0]'.
						case "$colcorrect_numlookups": // '1'.
						case "$colcorrectenable": // '1'.
						case "$color_flow_uvscale": // '100'.
						case "$color_ring_inner": // '[1 0.5 0.5]'.
						case "$color_ring_inner_intensity": // '0.9'.
						case "$color_ring_outer": // '[1 0.5 0.5]'.
						case "$color_ring_outer_intensity": // '0.91'.
						case "$color2": // '.485 .485 .485'. '[1.1 0.7 0]'. '[3.2 3.2 3.2]'. '{255 80 32}'. '{30.0 30.0 30.0}'. '{68.0 53.0 59.0 $model 1'. '{96 96 6}'. '3 3 3'.
						case "$color2base": // '{ 255 255 180 }'. '{50 150 255}'.
						case "$colormasktexture": // 'models\player\mp_scientist_hev\v_hand_sheet_colormask'.
						case "$colormodenabled": // '1'.
						case "$colortint": // '[1.0 1.3 1.6]'.
						case "$colourbase": // '1'.
						case "$combine_mode": // '3'. '0'. '2'. '1'.
						case "$comparez": // '0'.
						case "$corecolortexture": // 'models/props_lambda/superportal/eg_superportal_warpcolor_inverted'. 'models/props_lambda/superportal/superportal_warpcolor'.
						case "$coreglow1": // '.2'.
						case "$coreglow2": // '0'.
						case "$corneabumpstrength": // '0.75'. '1.0'.
						case "$corneatexture": // 'models/eli/eye-cornea'. 'models/humans/eye-cornea'. 'models/kleiner/eye-cornea'.
						case "$crackmaterial":
						case "$csmdepthatlassampler": // '_rt_csmshadowdepth'.
						case "$cstrike": // '1'.
						case "$cull": // '0'.
						case "$curr": // '0.0'.
						case "$darkpass": // '1'.
						case "$darkpassdimscale": // '1.5'.
						case "$darkpassluminancepow": // '0.8'.
						case "$debug": // '0'.
						case "$decal":
						case "$decalfadeduration": // '2.00'.
						case "$decalfadetime": // '0.00'. '1.0'.
						case "$decalscale":
						case "$decalsecondpass": // '1'.
						case "$decay": // '0'.
						case "$decayrate": // '0'.
						case "$density": // '0'.
						case "$depthblend": // '0'. '1'.
						case "$depthblendscale": // '100'. '1500'. '500'. '7'. '8'.
						case "$depthtest": // '0'.
						case "$detail2": // 'shadertest/detail'.
						case "$detailblendfactor3": // '0.6'.
						case "$detailframe": // '0'.
						case "$detailtexturetransform": // 'center .5 .5 scale 1 1 rotate 90 translate 0 0'.
						case "$detailtint": // '[0.5 0.5 0.5]'.
						case "$diameter": // '100'.
						case "$dilation": // '0.5'. '0.7'.
						case "$dirttexture": // 'dev\xbow_lens_dirt.vtf'.
						case "$disable_color_writes": // '1'.
						case "$disperserate": // '0'.
						case "$distance_alpha": // '0.0'.
						case "$distancealpha": // '1'.
						case "$distancealphafromdetail": // '1'.
						case "$distanceclamped": // '0'.
						case "$distanceinverted": // '0'.
						case "$dmax": // '700'.
						case "$dmin": // '600'.
						case "$dof_depth_texture": // '_rt_fullframedepth'.
						case "$dof_enable": // '0'.
						case "$dof_parms": // '[0 0 0 0]'.
						case "$dof_pass": // '0'.
						case "$dpscale": // '7.0'.
						case "$dtrail_color": // '[1 1 1]'. '[0 0 0.2]'.
						case "$dtrail_fade": // '1'.
						case "$dtrail_life": // '0.75'. '5'.
						case "$dtrail_shrink": // '1'. '0'
						case "$dtrail_textile": // '0.01'.
						case "$dtrail_width": // '16'.
						case "$dualsequence": // '1'.
						case "$dudvframe":
						case "$dudvmap":
						case "$edge_softness": // '0'. '0.5'.
						case "$edgesoftnessend": // '.45148'.
						case "$edgesoftnessstart": // '.55'.
						case "$emissiveblendbasetexture": // 'models/props_xen/foliage/xenplant_tendrilite_s_emis'. 'models/props_xen/interloper/factory_monitor_screen'. 'models/props_xen/interloper/healing_shower_display_d'. 'models/props_xen/interloper/uranium_crystal_large_mask'. 'models/props_xen/xen_tech/int_aneurysm_vein_s'. 'models\gibs\props_lab\lab_teleported_emissive'. 'models\props_xen\foliage\thornroot_set01_emissivemask'. 'models\props_xen\gonarch_bomb_exp'. 'models\props_xen\hub\tube_lava_exp'. 'models\weapons\v_rpg\rpg_glow'. 'models\xenians\protozoan\protozoan_reticulum2'.
						case "$emissiveblendenabled": // '0'. '1'.
						case "$emissiveblendflowtexture": // 'vgui/white'. 'vgui\white'.
						case "$emissiveblendscrollvector": // '[0.10 0.10]'. '[0.8 0.4]'.
						case "$emissiveblendstrength": // '.075'. '0.5'. '16.0'. 5.0'. '8'.
						case "$emissiveblendstrength1": // '2'.
						case "$emissiveblendstrength2": // '0'.
						case "$emissiveblendtexture": // 'models/props_xen/xen_teleporter3_glow'. 'models\props_xen\glow_red6'. 'models\xenians\protozoan\protozoan_energy'. 'vgui\white'.
						case "$emissiveblendtint": // '[1 0 0.125]'. '{243 184 143}'
						case "$emissiveblendtintbase": // '2'.
						case "$emmissive": // '1'.
						case "$enable_blending": // '0'.
						case "$endalpha": // '0'.
						case "$endfadesize": // '.575'
						case "$entityorigin": // '[ 0.000000 0.000000 0.000000 ]'. '{2688 12139 5170}'.
						case "$envcontrast": // '1'.
						case "$envmap2": // 'env_cubemap'.
						case "$envmapcameraspace": // '0'. '1'.
						case "$envmapfalloff": // '1'.
						case "$envmapframe": // '0'.
						case "$envmapfresnel": // '1'. '-1'. '-4.0000002384'. '8'.
						case "$envmapfresnelminmaxexp": // '[0.1 3 3]'.
						case "$envmapmask2": // 'rocks/coroded/rock38a_s_normal'.
						case "$envmapmaskframe": // '0'.
						case "$envmapmaskscale": // '10'. '4'. '5'.
						case "$envmapmasktransform": // '[ 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 ]'.
						case "$envmapsphere": //int
						case "$exp1": // '0'.
						case "$exp2": // '1'.
						case "$exposure": // '0'.
						case "$exposure_texture": // '_rt_teenyfb0'.
						case "$eyeballradius": // '0.5'. '1.25'. '1.9'. '3.25'.
						case "$eyeorigin": // '[ 0.000000 0.000000 0.000000 ]'.
						case "$eyeup": // '[ 0.000000 0.000000 0.000000 ]'.
						case "$fade_distance": // '768.0'. '7680.0'.
						case "$fade_min": // '0.1'. '1.0'.
						case "$fade_scale": // '0.5'. '0.9'.
						case "$fadedistance": // '800'.
						case "$far_alpha": // '0.0'.
						case "$fbtexture": // '_rt_fullframefb'.
						case "$final_distance_alpha": // '0.0'.
						case "$flare_chroma_distortionx": // '0.45'.
						case "$flare_chroma_distortiony": // '0.45'.
						case "$flare_chroma_distortionz": // '0.45'.
						case "$flare_dispersal": // '1'.
						case "$flare_halo_width": // '0.75'.
						case "$flashlightnolambert": // '1'.
						case "$flat": // '0'. '1'.
						case "$flatspeed": // '0'.
						case "$flow_bumpstrength": // '0.3'.
						case "$flow_color": // '[.025 .78 .75]'.
						case "$flow_debug": // '0'.
						case "$flow_lerpexp": // '1.5'. '1.5'. '3.5'.
						case "$flow_noise_scale": // '0.0078125'.
						case "$flow_noise_texture": // 'effects/fizzler_noise'.
						case "$flow_timeintervalinseconds": // '1.25'.
						case "$flow_timescale": // '0.05'.
						case "$flow_uvscrolldistance": // '0.035'.
						case "$flow_vortex_color": // '[0.64 2.058 2.56]'.
						case "$flow_vortex_size": // '35'.
						case "$flow_worlduvscale": // '0.008'. '0.01'. '550'. '200.0'. '0.015625'.
						case "$flowbounds": //texture effects/xen_shield_bounds2
						case "$flowmap": //texture effects/fizzler_flow
						case "$flowmapscrollrate": // '[.0475 .133]'.
						case "$flowmaptexcoordoffset": // '1.0'.
						case "$fogcolor":
						case "$fogenable":
						case "$fogend":
						case "$fogstart":
						case "$forcecheap": // '0'. '1'.
						case "$forceexpensive":
						case "$forcerefract": // '1'.
						case "$frameminusten": // '1'.
						case "$freeze": // '0'.
						case "$fresnelpower": // '0'. '1'.
						case "$fresnelreflection": // '1'. '0'. '0.25'. '.25'. '.3'.
						case "$gammacolorread": // '1'.
						case "$gbuffer_force": // '1'.
						case "$glassenvmap": // 'shadertest/gooinglass_envglass'.
						case "$glassenvmaptint": // '[ 1.00 1.00 1.00 ]'.
						case "$glint": // ''.
						case "$glintu": // '[ 0.000000 0.000000 0.000000 0.000000 ]'.
						case "$glintv": // '[ 0.000000 0.000000 0.000000 0.000000 ]'.
						case "$glow": // '1'.
						case "$glowalpha": // '0.5'. '1.0'.
						case "$glowcolor": // '[0 0 0]'.
						case "$glowend": // '.5'. '0.5'.
						case "$glowstart": // '.1'. '0'.
						case "$glowx": // '-0.01'.
						case "$glowy": // '-0.01'.
						case "$gnoise": // '1'.
						case "$groundmax": // '-0.1'.
						case "$groundmin": // '-0.3'.
						case "$groupbame": // 'bubble'.
						case "$halflambert":
						case "$halflambert_gbuffer_off": // '1'.
						case "$halfwidth": // '0.1'.
										   //case "$hdrbasetexture": // 'skybox/xen_sky_6_yellow_hdrbk'.
										   //case "$hdrbasetexture": // 'skybox/xen_sky_6_yellow_hdrdn'.
										   //case "$hdrbasetexture": // 'skybox/xen_sky_6_yellow_hdrft'.
										   //case "$hdrbasetexture": // 'skybox/xen_sky_6_yellow_hdrlf'.
										   //case "$hdrbasetexture": // 'skybox/xen_sky_6_yellow_hdrrt'.
										   //case "$hdrbasetexture": // 'skybox/xen_sky_6_yellow_hdrup'.
						case "$hdrbasetexture": // 'skybox\bm_sky_underground_00hdr'. 'skybox\bm_sky_underground_00hdr'.
												//case "$hdrcompressedtexture": // 'skybox\sky_st_dusk_01_hdr_bk'.
												//case "$hdrcompressedtexture": // 'skybox\sky_st_dusk_01_hdr_ft'.
												//case "$hdrcompressedtexture": // 'skybox\sky_st_dusk_01_hdr_lf'.
												//case "$hdrcompressedtexture": // 'skybox\sky_st_dusk_01_hdr_rt'.
												//case "$hdrcompressedtexture": // 'skybox\sky_st_dusk_01_hdr_up'.
						case "$hdrcompressedtexture": // 'skybox\xen_sky_4_hdr_bk'. 'skybox\xen_sky_4_hdr_dn'. 'skybox\xen_sky_4_hdr_ft'. 'skybox\xen_sky_4_hdr_lf'. 'skybox\xen_sky_4_hdr_rt'. 'skybox\xen_sky_4_hdr_up'.
						case "$heightmap": // 'models/props_xen/lpst_exp'. 'xen/moss00_h'. 'xen/wet_cave_floor_h'. 'xen/xen_rock_wall09a_h'. 'xen/xen_rockcliff_01a_normal_height'.
						case "$highblink": // '0'.
						case "$horizontal_scale": // '0.56'. '0.85'. '1.25'.
						case "$hundred": // '100'.
						case "$ignorevertexcolors": // '1'.
						case "$ignorez": // '1'. '0'.
						case "$inner_ring_alpha_end": // '1.0'.
						case "$inner_ring_alpha_start": // '1.0'.
						case "$inner_ring_min_lerp_delta": // '0.2'.
						case "$intro": // '0'.
						case "$invertphongmask": // '1'.
						case "$iris": // 'models/alyx/pupil_l'. 'models/gman/eye-iris-greenright'. 'models/vortigaunt/new_vort_eye'. 'models/vortigaunt/pupil'.
						case "$irisframe": // '0'.
						case "$irisu": // '[ 0.000000 0.000000 0.000000 0.000000 ]'.
						case "$irisv": // '[ 0.000000 0.000000 0.000000 0.000000 ]'.
						case "$j_b_halfwidth": // '0'. '2'.
						case "$j_b_noise": // '0'.
						case "$j_basescale": // '1'. '2'.
						case "$j_s_halfwidth": // '0'. '0.05'.
						case "$j_s_noise": // '0'.
						case "$j_t_halfwidth": // '0'. '0.25'.
						case "$j_threshold": // '1'. '3'.
						case "$jumpchance": // '0.0'. '0.025'.
						case "$jumpchancethreshold": // '0.95'. '0.99'.
						case "$jumpdistance": // '0.0'. '0.25'.
						case "$kernel": // '1'.
						case "$largeamount": // '1'.
						case "$leakamount": // '5'.
						case "$leakcolor": // '[0.5 1.0 0.5]'.
						case "$leakforce": // '10'.
						case "$leaknoise": // '0;'.
						case "$lightpass": // '1'.
						case "$lightwarptexture":
						case "$linearread_basetexture": // '1'.
						case "$linearread_texture1": // '0'.
						case "$linearread_texture2": // '0'.
						case "$linearwrite": // '1'.
						case "$lowblink": // '0'.
						case "$lowqualityflashlightshadows": // '1'.
						case "$lumblendfactor2": // '0.25'.
						case "$lumblendfactor3": // '0.3'.
						case "$lumblendfactor4": // '0.25'.
						case "$maskscale": // '[ 1.00 1.00 1.00 ]'.
						case "$matchinverted": // '0'.
						case "$material": // 'decals/decals_lit'. 'decals/decals_mod2x'. 'particle/particle_composite'.
						case "$maxlight": // '0.0'. '6'. '255'. '0.8'.
						case "$maxlumframeblend1": // '0'.
						case "$maxlumframeblend2": // '1'.
						case "$maxreflectivity": // '1.0'.
						case "$maxsize": // '.002'. '.25'. '.4'. '0.00001'.
						case "$mean": // '0.6'.
						case "$minlight": // '0.25'.
						case "$minphase1": // '0'.
						case "$minphase2": // '0'.
						case "$minreflectivity": // '0.5'. '1.0'.
						case "$minsize": // '.00075'. '0.75'.
						case "$mistbasebright": // '3.2'. '4.4'. '6.4'. '6.8'.
						case "$mistbrightmulti": // '1'.
						case "$mistbrightness": // '0.125'.
						case "$mistcolour": // '{255 255 255}'.
						case "$mistrotaterate": // '0.0'.
						case "$mistspeed": // '1'.
						case "$mistspeedmulti": // '0'.
						case "$mistspeedneg": // '0'.
						case "$mistwobble": // '0.1'.
						case "$mod": // '0.1'.
						case "$mod2x": // '1'.
						case "$model":
						case "$modelmaterial": // 'decals/yblood1'.
						case "$modulate": // '1'.
						case "$modulate_intensity": // '1.2'.
						case "$moss_angle_falloff": // '1'. '1.0'.
						case "$moss_angle_phi": // '180'. '60'. '70'.
						case "$moss_angle_theta": // '20'. '40'. '90'.
						case "$moss_blend_factor": // '0.6'. '1'.
						case "$moss_enable": // '1'.
						case "$moss_ref_direction": // '[0 0 1]'. '[-1 0.5 -0.5]'. '[1 1 1]'.
						case "$moss_scale": // '0.5'. '16'. '8'.
						case "$moss_scale_turn_on_absolute": // '1'.
						case "$moss_texture": // 'models/props_xen/moss_test/bubblemoss'. 'models/props_xen/moss_test/sphereofchon_d'.
						case "$motionblurinternal": // '[0 0 0 0]'.
						case "$moveresult": // '30'.
						case "$multipass":
						case "$multiplyby_max_hdr_overbright": // '1'. '0'.
						case "$multiplybyalpha": // '1'. '0'.
						case "$multiplybycolor": // '1'. '0'.
						case "$near_alpha": // '0.0'.
						case "$no_draw": // '0'. '1'.
						case "$no_fullbright":
						case "$noalphamod": // '0'.
						case "$nocompress": // '1'.
						case "$nodecal":
						case "$nodiffusebumplighting": // '1'.
						case "$noenvmapmip": // '1'.
						case "$nofog": // '0'. '1'.
						case "$nofresnel": // '1'.
						case "$noise_alpha": // '0.0'.
						case "$noise_enable": // '1'.
						case "$noise_luminance_weight": // '0.2'.
						case "$noise_range": // '0.25'.
						case "$noise_strength": // '0'.
						case "$noise_texture": // 'dev/postprocess_noise'.
						case "$noisechoice": // '0'.
						case "$nojump": // '0.0'.
						case "$nolod":
						case "$nomip": // '1'.
						case "$nooverbright": // '1'.
						case "$normalmap2": // 'liquids\flowingwater_normal'. 'liquids\ripplingwater_normal'. 'models/props_bounce/waterfall_01r_n2'. 'xen/xen_c1a4a_dirt_001a_ssbump'. 'xen/xen_c1a4a_rock_002a_ssbump'.
						case "$normalmapalpha": // '1'.
						case "$normalmapalphaenvmapmask2": // '1'.
						case "$normalmapenvmapmask": // '1'.
						case "$noscale": // '1'.
						case "$nosrgb": // '1'.
						case "$nowritez": // '1'.
						case "$offset": // '0'.
						case "$offsetx": // '0.078125'.
						case "$one": // '1'. '2'.
						case "$opaque_distance": // '192.0'.
						case "$opaquetexture": // '0'.
						case "$orientation": // '3'.
						case "$outer_ring_alpha_end": // '1.0'.
						case "$outer_ring_alpha_start": // '1.0'.
						case "$outer_ring_min_lerp_delta": // '0.2'.
						case "$outline": // '0'. '1'.
						case "$outlinealpha": // '.05'. '.95'. '1'.
						case "$outlinecolor": // '[.04 .0625 .01]'.
						case "$outlineend0": // '0.49'. '0.59'. '0.7'.
						case "$outlinestart0": // '0.415'. '0.45'. '0.55'.
						case "$overbrightfactor": // '0.5'. '3.5'. '4'. '5'.
						case "$parallaxmap": // 'brick/brickwall048a_height'.
						case "$parallaxmapscale": // '.005'. '.0125'. '.354'.
						case "$parallaxstrength": // '0.25'. '0.5'.
						case "$perparticleoutline": // '0'. '1'.
						case "$phongdisablehalflambert": // '1'.
														 //case "$pixshader": // 'appchooser360movie_ps20b'.
														 //case "$pixshader": // 'bloomadd_ps20'.
														 //case "$pixshader": // 'bms_debugdepthbuffer_ps20'.
														 //case "$pixshader": // 'bms_godrays_combine_ps20'.
														 //case "$pixshader": // 'bms_retroscanlines_ps20b'.
														 //case "$pixshader": // 'constant_color_ps20'.
														 //case "$pixshader": // 'copy_fp_rt_ps20'.
														 //case "$pixshader": // 'debugshadowbuffer_ps20'.
														 //case "$pixshader": // 'fade_blur_ps20'.
														 //case "$pixshader": // 'floattoscreen_notonemap_ps20'.
														 //case "$pixshader": // 'floattoscreen_ps20'.
														 //case "$pixshader": // 'lpreview_output_ps20'.
														 //case "$pixshader": // 'lpreview1_ps20'.
														 //case "$pixshader": // 'luminance_compare_ps20'.
														 //case "$pixshader": // 'sample4x4_ps20'.
														 //case "$pixshader": // 'sample4x4delog_ps20'.
														 //case "$pixshader": // 'sample4x4log_ps20'.
														 //case "$pixshader": // 'sample4x4maxmin_ps20'.
														 //case "$pixshader": // 'vertexcolor_ps20'.
						case "$pixshader": // 'sample4x4_blend_ps20'.
						case "$player_distance": // '0.0'.
						case "$playerdistance": // '0'.
						case "$playerdistance2": // '1'.
						case "$playerproximity1": // '0.0'.
						case "$playerunclamped": // '0.0'.
						case "$playerview1": // '0.0'.
						case "$polyoffset": // '1'.
						case "$pommaxsamples": // '100'. '200'. '400'.
						case "$pomminsamples": // '1'. '10'. '100'. '200'.
						case "$pomscale": // '0.005'.
						case "$pos": // '256  576'.
						case "$powerfunction": // '[2.5 5 8]'.
						case "$primarycolor": // '[1 1 1]'.
						case "$ps_mode": // '1'.
						case "$pseudotranslucent": // '1'.
						case "$pulsate_alpha": // '0.0'.
						case "$radius_portal_core": // '0.9'.
						case "$radius_ring_inner": // '1'.
						case "$radius_ring_outer": // '1.0'.
						case "$raytracesphere": // '0'. '1'.
						case "$receiveflashlight": // '1'.
						case "$reflectamount":
						case "$reflectblendfactor": // '0.4'.
						case "$reflectentities":
						case "$reflectivity": // '[0.29 0.29 0.29]'.
						case "$reflecttexture":
						case "$reflecttint":
						case "$refractamount":
						case "$refractionamount": // '[.005 .005]'.
						case "$refracttexture":
						case "$refracttint":
						case "$refracttinttexture":
						case "$rgb_thresholda": // '0'.
						case "$rgb_thresholdb": // '0.35'.
						case "$rgb_thresholdg": // '0.35'.
						case "$rgb_thresholdr": // '0.35'.
						case "$rimboost": // '1'.
						case "$rimexponent": // '3'. '40'.
						case "$rimlight":
						case "$rimlightboost":
						case "$rimlightexponent":
						case "$rimmask": // '1'.
						case "$saturation": // '0'.
						case "$scale":
						case "$scale2": // '[1 0.25]'.
						case "$scaleedgesoftnessbasedonscreenres": // '1'.
						case "$scaleoutlinesoftnessbasedonscreenres": // '1'.
						case "$scanlinecolorboost": // '1.5'.
						case "$scanlinesizeratio": // '128'.
						case "$scanlinetexture": // 'dev/hud_scanline'.
						case "$scanlineverticalscale": // '1'.
						case "$scanlineverticalscroll": // '0'.
						case "$scroll": // '[1 0]'.
						case "$scroll1":
						case "$scroll2":
						case "$scrollrateresult": // '1'.
						case "$seamless_base": // '1'.
						case "$seamless_detail": // '0'. '1'.
						case "$seamless_scale":
						case "$secondarycolor": // '[1 1 1]'.
						case "$sellillum": // '1'.
						case "$sequence_blend_mode": // '1'.
						case "$shadername":
						case "$sharpness": // '1.0'. '0.5'.
						case "$sheenindex": // '0'.
						case "$sheenmap": // 'cubemaps\cubemap_sheen001'.
						case "$sheenmapmask": // 'effects\animatedsheen\animatedsheen0'.
						case "$sheenmapmaskframe": // '0'.
						case "$sheenmaptint": // '[ 1 1 1 ]'.
						case "$sheenpassenabled": // '1'. 
						case "$silhouettecolor": // '[0.3 0.3 0.5]'.
						case "$silhouettethickness": // '0.2'.
						case "$sineperiodx": // '40'.
						case "$sineperiodxmulti": // '0'.
						case "$sineperiody": // '80'.
						case "$sineperiodymulti": // '0'.
						case "$sinex": // '0'.
						case "$siney": // '0'.
						case "$single_sample": // '1'.
						case "$size": // '256 256'.
						case "$slide": // '[0.0 0.0]'.
						case "$smallamount": // '.1'.
						case "$softedges": // '0'. '1'.
						case "$softwareskin": // '0'.
						case "$solidtexture": // '_rt_special'.
						case "$sourcemrtrendertarget": // '_rt_fullframefb'.
						case "$specmap_texture": // 'models/props_xen/int_aneurysm_vein_s'.
						case "$specularcolor": // '[1 1 1]'. '0.971519    0.959915    0.915324'.
						case "$speculargloss": // '1'. '128'.
						case "$spheretexkillcombo": // '0'.
						case "$splinetype": // '1'. '2'.
						case "$spriteorientation":
						case "$spriteorigin":
						case "$spriterendermode": // '4'. '5'. '8'.
						case "$spritescale": // '1'.
						case "$spritesize": // '140'. '19'. '500'.
						case "$startfadesize": // '.425'.
						case "$starttime": // '16'.
						case "$steps": // '12'.
						case "$stretch": // 'shadertest/basetexture'.
						case "$subdivsize":
						case "$sunposition": // '[0 0 0]'.
						case "$surfaceprop":
						case "$surfaceprop1": // 'dirt'.
						case "$surfacepro2": // 'concrete'. 'rock'.
						case "$surfaceprop2":
						case "$surfaceprop3": // 'carpet'. 'flesh'. 'glass'. 'inttower'. 'mud'. 'rock'. 'sand'. 'wood'.
						case "$surfaceprop4": // 'carpet'. 'dirt'. 'flesh'. 'glass'. 'inttower'. 'mud'. 'rock'. 'sand'. 'wood'.
						case "$surfacepro4p": // 'sand'.
						case "$surfaceproperty": // 'rock'.
						case "$sval": // '0'.
						case "$switch": // '0'. '0.8'. '1'.
						case "$teammatch": // '0'.
						case "$temp": // '[0 0]'. '0'. '1'.
						case "$temp1": // '0'.
						case "$temp2": // '0'.
						case "$tempmax": // '0.22'.
						case "$tempmin": // '0.21'.
						case "$tempvec": // '[0 0]'.
						case "$ten": // '1500'. '22'. '23'. '24'.
						case "$tertiarycolor": // '[1 1 1]'.
						case "$tex_cube": // 'cubemaps/cube_nihil_chamber_faf'. 'dev/devskycube'.
						case "$tex2offset": // '[0 0]'.
						case "$tex2scale": // '0'.
						case "$texoffset": // '[0 0]'.
						case "$texs0": // '_rt_resolvedfullframedepthrt'.
						case "$texscale": // '.25'. '0.5'. '1'.
						case "$texture1": // '_rt_albedo'. '_rt_normal'. '_rt_smallfb0'. '_rt_smallfb1'.
						case "$texture1_blendend": // '0.6'.
						case "$texture1_blendmode": // '0'.
						case "$texture1_blendstart": // '0.4'.
						case "$texture1_bumpblendfactor": // '1'.
						case "$texture1_lumend": // '0.75'
						case "$texture1_lumstart": // '0.1'.
						case "$texture1_uvscale": // '[.75 .75]'.
						case "$texture2_blendend": // '0.9'.
						case "$texture2_blendmode": // '0'.
						case "$texture2_blendstart": // '0.45'.
						case "$texture2_bumpblendfactor": // '0.8'.
						case "$texture2_lumend": // '0.45'..
						case "$texture2_lumstart": // '0.07'
						case "$texture2_uvscale": //float2
						case "$texture2blendmode": // '0'.
						case "$texture2scale": // '0'. '1'. '10.0'.
											   //case "$texture2transform": // 'center .5 .5 scale .6 .6 rotate 30 translate 0 0'.
						case "$texture2transform": // 'center .5 .5 scale 1 1 rotate 0 translate 0 0'.
						case "$texture2z_lumend": // '.5'. '0.02'. '0.5'.
						case "$texture3": // '_rt_accbuf_0'.
						case "$texture3_blen`dstart": // '0.4'.
						case "$texture3_blendend": // '.85'. '0.9'. ''. '0'.
						case "$texture3_blendstart": // '0.43'
						case "$texture3_bumpblendfactor": // '0.8'.
						case "$texture3_lumend": // '0.09'.
						case "$texture3_lumstart": // '0.15'
						case "$texture3_uvscale": // '[1.25 1.25]'.
						case "$texture4_blendend": // '.5'. '0.4'. '0.99'. '1'. '1.0'.
						case "$texture4_blendmode": // '0'. '0'.
						case "$texture4_blendstart": // '0.0'. '0.15'. '0.90'.
						case "$texture4_bumpblendfactor": // '0.25'. '0.3'. '0.7'. '1'.
						case "$texture4_lumend": // '.7'. '0'. '0.0'. '0.04'. '0.15'. '0.8'.
						case "$texture4_lumstart": // '0'. '0.00'. '0.29'. '0.8'.
						case "$texture4_uvscale": // '[.25 .25]'. '[0.5 0.5]'. '[2 2]'. '[2.0 2.0]'.
						case "$time": // '0'. '0.0'.
						case "$timer": // '0'.
						case "$tint": // '[.02 .02 .02]'.
						case "$tinta": // '[3 1 0]'.
						case "$tintb": // '[.15 .15 .15]'.
						case "$tintc": // '[0.6 0.3 0.3]'.
						case "$tinttemp": // '[0 0 0]'.
						case "$tmp1": // '0.0'.
						case "$tmp2": // '0.0'.
						case "$tmp3": // '0.0'.
						case "$tmp4": // '0.0'.
						case "$tmp5": // '0.0'.
						case "$tooltexture": // 'shadertest/shadertest_env'.
						case "$translate":
						case "$translate1": // '[0.0 0.0]'.
						case "$translate2": // '[0 0]'. '[0.0 0.0]'.
						case "$translucency": // '0'.
						case "$transluscent": // '1'.
						case "$transoffset": // '0.14'. '0.28'. '0.42'. '0.56'. '0.7'. '0.84'.
						case "$transx": // '0'.
						case "$transy": // '0'.
						case "$treesway": // '0'. '1 (0 1 or 2 but 2 works best)'. '1'. '2 (0 1 or 2 but 2 works best)'. '2'.
						case "$treeswayfalloffexp": // '.75'. '128'. '5'. '6'. '80'.
						case "$treeswayheight": // '0'. '10'. '-1024'. '32.0'. '-5'. '-512'. '60'.
						case "$treeswayradius": // '0.01'. 0.25'. '20.0'. '25'. '5'.
						case "$treeswayscrumblefalloffexp": // '1'. '128'. '32.0'. '5'. '6'. '7'.
						case "$treeswayscrumblefrequency": // '0.01'. '10'. '2'. '2.0'. '3400'. '60'. '64'.
						case "$treeswayscrumblespeed": // '.05'. '.5'. '0.2'. '1'. '12'. '16'. '2'. '3'. '4'. '5'.
						case "$treeswayscrumblestrength": // '.015'. '0.101'. '0.7'. '1.0'.
						case "$treeswayspeed": // '.05'. '.5'. '0.050505'. '1'. '2'.
						case "$treeswayspeedhighwindmultiplier": // '.05'. '.25'. '0'. '1'. '1.1'. '2'. '6'.
						case "$treeswayspeedlerpend": // '.5'. '1'. '4200000.0'. '6.0'. '60.0'.
						case "$treeswayspeedlerpstart": // '0'. '2.0'. '2000.0'. '40.0'. '696969.0'.
						case "$treeswaystartheight": // '0.05'. '1'. '100'. '-25'. '5'.
						case "$treeswaystartradius": // '.01'. '0.25'. '1'. '1.0'.
						case "$treeswaystrength": // '0.020205'. '0.05'. '1.0'.
												  //case "$underwateroverlay": // 'effects/causticswater_warp'.
												  //case "$underwateroverlay": // 'effects/water_warp01'.
												  //case "$underwateroverlay": // 'effects\water_warp01'.
						case "$underwateroverlay": // 'effects\water_warp01'.
						case "$use_in_fillrate_mode": // '0'. '1'.
						case "$vertical_scale": // '0.825'. '0.9'. '1'.
						case "$viewmodelhands": // '1'.
						case "$vignette_min_bright": // '1'.
						case "$vignette_power": // '1.0'.
						case "$warpindex_diff": // '2'. '4'.
						case "$warpindex_spec": // '2'.
						case "$warpparam": // '0.000000'.
						case "$waterbasefactor":// float
						case "$waterbasemovementdist": //float
						case "$waterbasemovementfreq": //float
						case "$watercolor": // color
						case "$waterdepth": // float
						case "$watermurkiness":// float
						case "$waterspecularmax": // float
						case "$waterspecularmin": // float
						case "$watertimefreq1": // float
						case "$watertimefreq2": // float
						case "$waterwaveheight": //float
						case "$waterwavelength": // float
						case "$wave": // '10.00'.
						case "$waves_freq": // '20.0'.
						case "$waves_rot_speed": // '4.0'.
						case "$waves_size_inner_core": // '0.01'.
						case "$waves_size_inner_core_const_a": // '0.975'.
						case "$waves_size_override_const_a": // '0'.
						case "$waves_size_ring_inner": // '0.01'.
						case "$waves_size_ring_inner_const_a": // '0.96'.
						case "$waves_size_ring_outer": // '0.01'.
						case "$waves_size_ring_outer_const_a": // '0.98'.
						case "$web_burn_factor": // '0'.
						case "$web_burn_time": // '1'.
						case "$web_glow_color": // '[0.54 2 0.21]'.
						case "$web_glow_threshold": // '.85'. '0.23'.
						case "$web_mask_texture": // '1'. 'dev/burnableweb_mask'.
						case "$web_shader": // '1'.
						case "$weight": // '0'.
						case "$wetbrightnessfactor": // '0.4'.
						case "$woodcut": // '0.00'.
						case "$wrinkle": // 'shadertest/basetexture'.
						case "$writealpha": // '0'.
						case "$writedepth": // '0'.
						case "$writez": // '0'. '1'.
						case "$x360appchooser": // '1'.
						case "$xo_b_halfwidth": // '0'. '0.035'.
						case "$xo_b_noise": // '0'.
						case "$xo_s_halfwidth": // '0'. '0.001'.
						case "$xo_s_noise": // '0'.
						case "$xo_t_halfwidth": // '0'. '0.0'.
						case "$xo_threshold": // '0.1'. '0'.
						case "$zero": // '0'.
						case "$znearer": // '0'. '1'.
						case "$zoomanimateseq2": // '1.5'. '2'. '3'. '4'.
						case "c0_x": // '0'.
						case "c0_y": // '1'.
						case "c0_z": // '0'.
						case "ignorez": // '1'.
						case "nodecal": // '1'.
						case "nomip": // '1'.
						case "normalmapalphaenvmapmask": // '1'.
						case "proxies":
						{
							// no documentation?
							break;
						}

						case "$basenormalmap3": // 'brick/cinderblock_pink_01a_normal'. 'xen/xen_dirtfloor_001a_n'. 'xen/xen_int_sinew_02a_ssbump'.
						case "$basenormalmap4": // 'concrete\csrev_concretefloor_02_normal'. 'nature/bnc_dirt_rocky_large_001_norm'. 'xen/ext_interloper_tower_top_smooth_ssbump'.
						case "$basetexture4": // 'brick/brickwall001a'. 'concrete\csrev_concretefloor_02'. 'xen/xen_tree_roots_01a_d'. 'xen/xen_wetmud_01a_diff'.
											  //case "$basetexturetransform": // '[ 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 0.000000 0.000000 0.000000 0.000000 1.000000 ]'.
											  //case "$basetexturetransform": // 'center .5 .5 scale .5 .5 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 0.5 0.5 rotate 0  translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 1 1 rotate 0  translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 1 1 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 1 1 rotate 180 translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 1 1 rotate 90  translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 1 1 rotate -90 translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 1.5 1.5 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 2 2 rotate 0  translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 2 2 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center .5 .5 scale 2 2 rotate 90  translate 0 0'.
											  //case "$basetexturetransform": // 'center 0 0 scale 1 1 rotate 90 translate 0 0'.
											  //case "$basetexturetransform": // 'center 0 0 scale 1 1 rotate 90 translate 0 0'.
											  //case "$basetexturetransform": // 'center 0 0 scale 1 10 rotate 30 translate .2 .3'.
											  //case "$basetexturetransform": // 'center 0 0 scale 1 2 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center 0 0 scale 1 2 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center 0 0 scale 2 2 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center 0 0 scale 2 4 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center 0 0 scale 3 3 rotate 0 translate 0 0'.
											  //case "$basetexturetransform": // 'center 0.5 0.5 scale 0.67 0.67 rotate 0  translate 0 0'.
											  //case "$basetexturetransform": // 'center -0.5 -0.5 scale 1 1 $angle $translate'.
											  //case "$basetexturetransform": // 'center 1 1 scale 2 2 rotate 0 translate .50 0'.
											  //case "$basetexturetransform": // 'center 1 1 scale 2 2 rotate 0 translate 0 0'.
						case "$basetexturetransform": // 'center 2 2 scale 2 2 rotate 0 translate 0 0'.
						case "$cloudscale": // '[ 2.00 2.00 2.00 ]'.
						case "$detailblendfactor2": // '0.4'. '0.5'.
						case "$detailblendfactor4": // '0.4'. '0.5'.
						case "$envmapfrensel": // '1'.
						case "$flow_normaluvscale": // '0.008'. '200.0'. '550'.
						case "$outlineend1": // '0.5'. '0.59'.
						case "$outlinestart1": // '0.44'. '0.55'.
						case "$texture3_blendmode": // ''. '0'.
						case "vertexlitgeneric_dx9": // ''.
						case "$vertexalpha": // '0'. '1'.
						case "$vertexfog": // '1'.
						case "$vertextcolor": // '1'.
						case "$vertextransform": // '0'.
						case "$vertexcolor": // '0'/'1'.
						case "$vertexcolors": // '1'.
						case "$vertexcolormodulate": // '1'.
						{
							// no documentation?
							break;
						}

						//case "*/": // ''.
						//case "/*": // ''.
						//case "\\\\": // '$phongboost 2'. '$phongexponent 7'.
						//case "\\\\$halflambert": // '0'.
						//case "\\\\$phong": // '1'.
						//case "\\\\$phongfresnelranges": // '[.4 .8 30]'.
						//case "\\\\$phongtint": // '[1 1 1]'.
						case "$envmapcontrast":
						{
							// no documentation?
							break;
						}

						case "$envmap": { vmfMaterial.EnvMapTextureName = property.Value; break; }
						case "$envmapmask": { vmfMaterial.EnvMapMaskTextureName = property.Value; break; }
						case "$envmaptint": { vmfMaterial.EnvMapTint = TextParser.ParseColor(property.Value); break; }

						//case "$envmapcontrast": // '.5 .5 .5'.expected floating point value.
						//case "$envmapcontrast": // '[0.3 0.3 0.3]'.expected floating point value.
						//case "$envmapcontrast":			{ TryParseVector3Property(property, ref vmfMaterial.EnvMapContrast); break; }
						case "$envmapsaturation": { TryParseVector3Property(property, ref vmfMaterial.EnvMapSaturation); break; }
						case "$envmapmode": { TryParseIntProperty(property, ref vmfMaterial.EnvMapMode); break; }

						case "$detail": { vmfMaterial.DetailTextureName = property.Value; break; }

						case "$detailscale2": // '.25'.
						{
							// no documentation?
							break;
						}

						//case "$detailscale": // '[24 24]'.expected floating point value.
						//case "$detailscale": // '[ 32 32 ]'.expected floating point value.
						//case "$detailscale": // '[32 32]'.expected floating point value.
						//case "$detailscale": // '[ 10 5 ]'.expected floating point value.
						case "$detailscale": { TryParseVector2Property(property, ref vmfMaterial.DetailScale); break; }
						case "$detailblendfactor": { TryParseFloatProperty(property, ref vmfMaterial.DetailBlendFactor); break; }
						case "$detailblendmode": { TryParseEnumProperty(property, ref vmfMaterial.DetailBlendMode); break; }


						case "$translucent": { TryParseBoolProperty(property, ref vmfMaterial.Translucent); break; }

						//case "$additive": // '.3'.expected boolean value.
						case "$additive": { TryParseFloatProperty(property, ref vmfMaterial.Additive); break; }

						//case "$nocull": // ''.expected boolean value.
						case "$nocull": { TryParseBoolProperty(property, ref vmfMaterial.NoCull); break; }

						case "$blendmodulatetexture": { vmfMaterial.BlendModulateTextureName = property.Value; break; }

						case "$color": { vmfMaterial.Color = TextParser.ParseColor(property.Value); break; }

						case "$allowalphatocoverage": // '1'.
						case "$alpha": // '.85'. '1.000000'. '0.700000'. '0.800000'. '0.50'.
						case "srgb?$alpha": // '.375'.
						case "$alphatest2": // '1'.
						case "$alphatest2_texture": // 'models/props_xen/foliage/cavebulbs_alpha'.
						case "$alpha_bias": // '0.05'. '0.2'.
						case "$alphatested": // '1'. '0'.
						case "$alpharesult": // '1'.
						case "$alpharesultmin": // '1'.
						case "$alpharesultmax": // '1'.
						case "$alphaenvmapmask": // '1'.
						case "$alpha_blend": // '1'. '0'.
						case "$showalpha": // '0'. '1'.
						{
							// no documentation?
							break;
						}

						case "$alphatestreference": { TryParseFloatProperty(property, ref vmfMaterial.AlphaTestReference); break; }
						case "$alphatest": { TryParseBoolProperty(property, ref vmfMaterial.AlphaTest); break; }

						case "$frame": { TryParseIntProperty(property, ref vmfMaterial.BaseTextureFrame); break; }
						case "$basetexture": { vmfMaterial.BaseTextureName = property.Value; break; }
						case "$texture2":
						case "$basetexture2": { vmfMaterial.BaseTexture2Name = property.Value; break; }

						case "$basealphaenvmapmaskminmaxexp": // '[0 1 3]'.
						{
							// no documentation?
							break;
						}

						//case "$basealphaenvmapmask": // '1.0'.expected boolean value.
						case "$basealphaenvmapmask": { TryParseFloatProperty(property, ref vmfMaterial.BaseAlphaEnvMapMask); break; }

						case "$bumpframe": { TryParseIntProperty(property, ref vmfMaterial.BumpFrame); break; }
						case "$bumpmap": { vmfMaterial.BumpMapName = property.Value; break; }
						case "$bumpmap2": { vmfMaterial.BumpMap2Name = property.Value; break; }

						case "$ssbump2": // '1'.
						case "$ssbumpmathfix": // '1'.
						case "$ssbump": { TryParseBoolProperty(property, ref vmfMaterial.BumpSelfShadowing); break; }

						case "$normalmap": { vmfMaterial.NormalMapName = property.Value; break; }

						case "$normalalphaenvmapmask":
						case "$normalmapalphaenvmapmask": { TryParseBoolProperty(property, ref vmfMaterial.NormalMapAlphaEnvMapMask); break; }

						case "$selfillumfresnel": // '1'. '1.5'.
						case "$selfillumfrensel":
						case "$selfillumfrenselminmaxenp": // '[0.1 2.0 3.0]'.
						case "$selfillumfresnelminmaxexp": // '[.1 2.4 2]'.
						case "$selfillumcolor1": // '[0.5 0.5 0.5]'.
						case "$selfillumcolor2": // '[0 7 10]'.
						case "$selfillummaskscale": // '2'. '10'. '0.25'.
						{
							// no documentation?
							break;
						}

						case "$selfillum": { TryParseBoolProperty(property, ref vmfMaterial.SelfIllumination); break; }
						case "$selfillummask": { vmfMaterial.SelfIlluminationMask = property.Value; break; }
						case "$selfillumtint": { vmfMaterial.SelfIlluminationColor = TextParser.ParseColor(property.Value); break; }
						case "$selfillumtexture": { vmfMaterial.SelfIlluminationTexture = property.Value; break; }
						case "$selfillum_envmapmask_alpha": { TryParseFloatProperty(property, ref vmfMaterial.SelfIlluminationAlphaEnvMapMask); break; }

						case "$phong": { TryParseBoolProperty(property, ref vmfMaterial.Phong); break; }
						case "$phongwarptexture": { vmfMaterial.PhongWarpTextureName = property.Value; break; }
						case "$phongalbedotint": { TryParseBoolProperty(property, ref vmfMaterial.PhongAlbedoTint); break; }
						case "$phongtint": { vmfMaterial.PhongTint = TextParser.ParseColor(property.Value); break; }
						case "$phongboost": { TryParseFloatProperty(property, ref vmfMaterial.PhongBoost); break; }
						//case "$phongfresnelranges":	{ vmfMaterial.PhongFresnelRanges = TextParser.ParseVector3(property.Value); break; }

						case "$phongexponent": { TryParseFloatProperty(property, ref vmfMaterial.PhongExponentValue); break; }

						case "$phongfresnelranges":
						case "$phongexponentfactor": // '4'. '4.5999970436'.
						{
							// no documentation?
							break;
						}

						case "$phongexponenttexture": { vmfMaterial.PhongExponentTextureName = property.Value; break; }

						default:
						{
							ShowVMFMaterialPropertyError(property);
							break;
						}
					}
				}
			}
			return vmfMaterial;
		}

		private static void ShowVMFMaterialPropertyError(EntityHierarchyProperty property, string expected = "")
		{
			Debug.LogWarning($"Unrecognized material property '{property.Name}' with value '{property.Value}'. {expected}");
		}

		private static bool TryParseFloatProperty(EntityHierarchyProperty property, ref float? result, bool showErrorWhenNotFound = true)
		{
			if (!string.IsNullOrWhiteSpace(property.Value))
			{
				var stringValue = property.Value.Trim();
				if (stringValue.Length >= 2 &&
					stringValue[0] == '\'' &&
					stringValue[stringValue.Length - 1] == '\'')
				{
					stringValue = stringValue.Substring(1, stringValue.Length - 2);
				}

				if (TextParser.SafeParse(stringValue, out float value))
				{
					result = value;
					return true;
				}
			}

			if (showErrorWhenNotFound)
				ShowVMFMaterialPropertyError(property, "Expected floating point value.");
			return false;
		}

		private static bool TryParseVector3Property(EntityHierarchyProperty property, ref Vector3? result, bool showErrorWhenNotFound = true)
		{
			if (!string.IsNullOrWhiteSpace(property.Value))
			{
				var stringValue = property.Value.Trim();
				if (stringValue.Length >= 2 &&
					stringValue[0] == '\'' &&
					stringValue[stringValue.Length - 1] == '\'')
				{
					stringValue = stringValue.Substring(1, stringValue.Length - 2);
				}

				if (TextParser.SafeParse(stringValue, out float value))
				{
					result = new Vector3(value, value, value);
					return true;
				}

				if (stringValue.StartsWith("["))
				{
					if (stringValue.EndsWith("]"))
						stringValue = stringValue.Substring(1, stringValue.Length - 2);
					else
						stringValue = stringValue.Substring(1);
					var strings = stringValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (strings.Length == 3)
					{
						var string0 = strings[0];
						var string1 = strings[1];
						var string2 = strings[2];
						if (TextParser.SafeParse(string0, out float float1) &&
							TextParser.SafeParse(string1, out float float2) &&
							TextParser.SafeParse(string2, out float float3))
						{
							result = new Vector3(float1, float2, float3);
							return true;
						}
					}
				}
			}

			if (showErrorWhenNotFound)
				ShowVMFMaterialPropertyError(property, "Expected floating point value.");
			return false;
		}

		private static bool TryParseVector2Property(EntityHierarchyProperty property, ref Vector2? result, bool showErrorWhenNotFound = true)
		{
			if (!string.IsNullOrWhiteSpace(property.Value))
			{
				var stringValue = property.Value.Trim();
				if (stringValue.Length >= 2 &&
					stringValue[0] == '\'' &&
					stringValue[stringValue.Length - 1] == '\'')
				{
					stringValue = stringValue.Substring(1, stringValue.Length - 2);
				}

				if (TextParser.SafeParse(stringValue, out float value))
				{
					result = new Vector2(value, value);
					return true;
				}

				if (stringValue.StartsWith("["))
				{
					if (stringValue.EndsWith("]"))
						stringValue = stringValue.Substring(1, stringValue.Length - 2);
					else
						stringValue = stringValue.Substring(1);
					var strings = stringValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (strings.Length == 2)
					{
						var string0 = strings[0];
						var string1 = strings[1];
						if (TextParser.SafeParse(string0, out float float1) &&
							TextParser.SafeParse(string1, out float float2))
						{
							result = new Vector2(float1, float2);
							return true;
						}
					}
				}
			}

			if (showErrorWhenNotFound)
				ShowVMFMaterialPropertyError(property, "Expected floating point value.");
			return false;
		}

		private static bool TryParseIntProperty(EntityHierarchyProperty property, ref int? result, bool showErrorWhenNotFound = true)
		{
			if (!string.IsNullOrWhiteSpace(property.Value))
			{
				var stringValue = property.Value.Trim();
				if (stringValue.Length >= 2 &&
					stringValue[0] == '\'' &&
					stringValue[stringValue.Length - 1] == '\'')
				{
					stringValue = stringValue.Substring(1, stringValue.Length - 2);
				}

				if (!string.IsNullOrEmpty(stringValue))
				{
					if (Int32.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
					{
						result = value;
						return true;
					}
				}
			}

			if (showErrorWhenNotFound)
				ShowVMFMaterialPropertyError(property, "Expected integer value.");
			return false;
		}

		private static bool TryParseEnumProperty<T>(EntityHierarchyProperty property, ref T? result, bool showErrorWhenNotFound = true) 
			where T : struct
		{
			if (!string.IsNullOrWhiteSpace(property.Value))
			{
				var stringValue = property.Value.Trim();
				if (stringValue.Length >= 2 &&
					stringValue[0] == '\'' &&
					stringValue[stringValue.Length - 1] == '\'')
				{
					stringValue = stringValue.Substring(1, stringValue.Length - 2);
				}

				if (!string.IsNullOrEmpty(stringValue))
				{
					if (Int32.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
					{
						result = (T)Enum.ToObject(typeof(T), value);
						return true;
					}
				}
			}

			if (showErrorWhenNotFound)
				ShowVMFMaterialPropertyError(property, "Expected enum of type "+typeof(T).Name+" .");
			return false;
		}

		private static bool TryParseBoolProperty(EntityHierarchyProperty property, ref bool? result, bool showErrorWhenNotFound = true)
		{
			if (string.IsNullOrWhiteSpace(property.Value))
			{
				result = true;
				return true;
			}
				
			var stringValue = property.Value.Trim();
			if (stringValue.Length >= 2 &&
				stringValue[0] == '\'' &&
				stringValue[stringValue.Length - 1] == '\'')
			{
				stringValue = stringValue.Substring(1, stringValue.Length - 2);
			}

			if (!string.IsNullOrEmpty(stringValue))
			{
				if (Int32.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
				{
					result = (value == 1);
					return true;
				}
			}

			if (showErrorWhenNotFound)
				ShowVMFMaterialPropertyError(property, "Expected boolean value.");
			return false;
		}

	}
}