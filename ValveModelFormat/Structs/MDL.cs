using System;
using System.Collections.Generic;
using System.IO;
using DebuggerDisplayAttribute = System.Diagnostics.DebuggerDisplayAttribute;

using Debug = UnityEngine.Debug;
using Mathf = UnityEngine.Mathf;
using Bounds = UnityEngine.Bounds;
using Vector3 = UnityEngine.Vector3;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Quaternion = UnityEngine.Quaternion;
using Multiline = UnityEngine.MultilineAttribute;
using HideInInspector = UnityEngine.HideInInspector;

namespace Chisel.Import.Source.VPKTools
{
	// TODO: get rid of this
	internal static class SourceEngineConstants
	{
		public const int MaxNumLods = 8;
		public const int MaxNumBonesPerVert = 3;
		public const int OptimizedModelFileVersion = 7;
	};

	// TODO: get rid of this
	public class Lookup
	{
		readonly Dictionary<Type, Dictionary<long, object>> TypeOffsetLookup = new();

		public T Get<T>(long position) where T : class
		{
			var type = typeof(T);
			Dictionary<long, object> lookup;
			if (!TypeOffsetLookup.TryGetValue(type, out lookup))
			{
				lookup = new Dictionary<long, object>();
				TypeOffsetLookup[type] = lookup;
			}
			object obj;
			if (!lookup.TryGetValue(position, out obj))
				return null;
			return obj as T;
		}

		public void Set<T>(long position, T item) where T : class
		{
			var lookup = TypeOffsetLookup[typeof(T)];
			lookup[position] = item;
		}
	}


	// mstudioaxisinterpbone_t
	public class StudioAxisInterpolateBone
	{
		public int          control;// local transformation of this bone used to calc 3 point blend
		public int          axis;	// axis to check
		public Vector3[]    pos	 = new Vector3[6];	  // X+, X-, Y+, Y-, Z+, Z-
		public Quaternion[] quat = new Quaternion[6]; // X+, X-, Y+, Y-, Z+, Z-

		public static StudioAxisInterpolateBone LoadItem(BinaryReader reader, Lookup lookup, long offset)
		{
			if (offset == 0)
				return null;

			var item = lookup.Get<StudioAxisInterpolateBone>(offset);
			if (item != null)
				return item;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

			item = new StudioAxisInterpolateBone
			{
				control = reader.ReadInt32(),
				axis	= reader.ReadInt32(),
				pos = new []
				{
					reader.ReadVector3(),
					reader.ReadVector3(),
					reader.ReadVector3(),
					reader.ReadVector3(),
					reader.ReadVector3(),
					reader.ReadVector3()
				},
				quat = new []
				{
					reader.ReadQuaternion(),
					reader.ReadQuaternion(),
					reader.ReadQuaternion(),
					reader.ReadQuaternion(),
					reader.ReadQuaternion(),
					reader.ReadQuaternion()
				}
			};

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioAxisInterpolateBone>(offset, item);
			return item;
		}

		public static StudioAxisInterpolateBone[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioAxisInterpolateBone[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			int itemSize = 4 * ((6*4) + (6*3) + 2);
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				items[i] = LoadItem(reader, lookup, currentOffset);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
	

	// mstudioquatinterpinfo_t
	public class StudioQuaternionInterpolateInfo
	{
		public float	  invTolerance;	// 1 / radian angle of trigger influence
		public Quaternion trigger;		// angle to match
		public Vector3	  pos;			// new position
		public Quaternion quat;			// new angle
			
		public static StudioQuaternionInterpolateInfo LoadItem(BinaryReader reader, Lookup lookup, long offset)
		{
			if (offset == 0)
				return null;

			var item = lookup.Get<StudioQuaternionInterpolateInfo>(offset);
			if (item != null)
				return item;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

			item = new StudioQuaternionInterpolateInfo
			{
				invTolerance = reader.ReadSingle(),
				trigger      = reader.ReadQuaternion(),
				pos          = reader.ReadVector3(),
				quat         = reader.ReadQuaternion()
			};

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioQuaternionInterpolateInfo>(offset, item);
			return item;
		}

		public static StudioQuaternionInterpolateInfo[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			return null;
			#if false
			// TODO: figure out why this is broken
			StudioQuaternionInterpolateInfo[] items;
			try
			{
				items = new StudioQuaternionInterpolateInfo[count];
			}
			catch (Exception ex)
			{
				Debug.LogError($"count {count}");
				throw ex;
			}
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			int itemSize = (4+3+4+1) * 4;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				items[i] = LoadItem(reader, lookup, currentOffset);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
			#endif
		}
	};


	// mstudioquatinterpinfo_t
	public class StudioQuaternionInterpolateBone
	{
		public int control;// local transformation to check
		public StudioQuaternionInterpolateInfo[] triggers;

		public static StudioQuaternionInterpolateBone LoadItem(BinaryReader reader, Lookup lookup, long offset)
		{
			if (offset == 0)
				return null;

			var item = lookup.Get<StudioQuaternionInterpolateBone>(offset);
			if (item != null)
				return item;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

			item = new StudioQuaternionInterpolateBone
			{
				control  = reader.ReadInt32(),					
				triggers = StudioQuaternionInterpolateInfo.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32())
			};

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioQuaternionInterpolateBone>(offset, item);
			return item;
		}

		public static StudioQuaternionInterpolateBone[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioQuaternionInterpolateBone[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			int itemSize = 3 * 4;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				items[i] = LoadItem(reader, lookup, currentOffset);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// mstudioaimatbone_t
	public class StudioAimAtBone
	{
		public int	   parent;
		public int	   bone; 
		public Vector3 aimvector;
		public Vector3 upvector;
		public Vector3 basepos;
			
		public static StudioAimAtBone LoadItem(BinaryReader reader, Lookup lookup, long offset)
		{
			if (offset == 0)
				return null;

			var item = lookup.Get<StudioAimAtBone>(offset);
			if (item != null)
				return item;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

			item = new StudioAimAtBone
			{
				parent	  = reader.ReadInt32(),
				bone	  = reader.ReadInt32(),
				aimvector = reader.ReadVector3(),
				upvector  = reader.ReadVector3(),
				basepos   = reader.ReadVector3()
			};

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioAimAtBone>(offset, item);
			return item;
		}

		public static StudioAimAtBone[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioAimAtBone[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			int itemSize = (9 + 2) * 4;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				items[i] = LoadItem(reader, lookup, currentOffset);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// mstudioaimatbone_t
	public class StudioAimAtAttachment
	{
		public int     parent;
		public int     attachment;
		public Vector3 aimvector;
		public Vector3 upvector;
		public Vector3 basepos;
			
		public static StudioAimAtAttachment LoadItem(BinaryReader reader, Lookup lookup, long offset)
		{
			if (offset == 0)
				return null;

			var item = lookup.Get<StudioAimAtAttachment>(offset);
			if (item != null)
				return item;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

			item = new StudioAimAtAttachment
			{
				parent     = reader.ReadInt32(),
				attachment = reader.ReadInt32(),
				aimvector  = reader.ReadVector3(),
				upvector   = reader.ReadVector3(),
				basepos    = reader.ReadVector3()
			};

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioAimAtAttachment>(offset, item);
			return item;
		}

		public static StudioAimAtAttachment[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioAimAtAttachment[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			int itemSize = (9 + 2) * 4;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				items[i] = LoadItem(reader, lookup, currentOffset);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
	
	[Flags]
	public enum JiggbleBoneFlags
	{ 
		JIGGLE_IS_FLEXIBLE				= 0x01,
		JIGGLE_IS_RIGID					= 0x02,
		JIGGLE_HAS_YAW_CONSTRAINT		= 0x04,
		JIGGLE_HAS_PITCH_CONSTRAINT		= 0x08,
		JIGGLE_HAS_ANGLE_CONSTRAINT		= 0x10,
		JIGGLE_HAS_LENGTH_CONSTRAINT	= 0x20,
		JIGGLE_HAS_BASE_SPRING			= 0x40,
		JIGGLE_IS_BOING					= 0x80      // simple squash and stretch sinusoid "boing"
	}

	// mstudiojigglebone_t
	public class StudioJiggleBone
	{
		public JiggbleBoneFlags flags;

		// general params
		public float length; // how from from bone base, along bone, is tip
		public float tipMass;

		// flexible params
		public float yawStiffness;
		public float yawDamping;	
		public float pitchStiffness;
		public float pitchDamping;	
		public float alongStiffness;
		public float alongDamping;	

		// angle constraint
		public float angleLimit; // maximum deflection of tip in radians
	
		// yaw constraint
		public float minYaw; // in radians
		public float maxYaw; // in radians
		public float yawFriction;
		public float yawBounce;
	
		// pitch constraint
		public float minPitch; // in radians
		public float maxPitch; // in radians
		public float pitchFriction;
		public float pitchBounce;

		// base spring
		public float baseMass;
		public float baseStiffness;
		public float baseDamping;	
		public float baseMinLeft;
		public float baseMaxLeft;
		public float baseLeftFriction;
		public float baseMinUp;
		public float baseMaxUp;
		public float baseUpFriction;
		public float baseMinForward;
		public float baseMaxForward;
		public float baseForwardFriction;

		// boing
		public float boingImpactSpeed;
		public float boingImpactAngle;
		public float boingDampingRate;
		public float boingFrequency;
		public float boingAmplitude;


		public static StudioJiggleBone LoadItem(BinaryReader reader, Lookup lookup, long offset)
		{
			if (offset == 0)
				return null;

			var item = lookup.Get<StudioJiggleBone>(offset);
			if (item != null)
				return item;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

			item = new StudioJiggleBone
			{
				flags				= (JiggbleBoneFlags)reader.ReadInt32(),
				length				= reader.ReadSingle(),
				tipMass				= reader.ReadSingle(),
				yawStiffness		= reader.ReadSingle(),
				yawDamping			= reader.ReadSingle(),
				pitchStiffness		= reader.ReadSingle(),
				pitchDamping		= reader.ReadSingle(),
				alongStiffness		= reader.ReadSingle(),
				alongDamping		= reader.ReadSingle(),
				angleLimit			= reader.ReadSingle(),
				minYaw				= reader.ReadSingle(),
				maxYaw				= reader.ReadSingle(),
				yawFriction			= reader.ReadSingle(),
				yawBounce			= reader.ReadSingle(),
				minPitch			= reader.ReadSingle(),
				maxPitch			= reader.ReadSingle(),
				pitchFriction		= reader.ReadSingle(),
				pitchBounce			= reader.ReadSingle(),
				baseMass			= reader.ReadSingle(),
				baseStiffness		= reader.ReadSingle(),
				baseDamping			= reader.ReadSingle(),
				baseMinLeft			= reader.ReadSingle(),
				baseMaxLeft			= reader.ReadSingle(),
				baseLeftFriction	= reader.ReadSingle(),
				baseMinUp			= reader.ReadSingle(),
				baseMaxUp			= reader.ReadSingle(),
				baseUpFriction		= reader.ReadSingle(),
				baseMinForward		= reader.ReadSingle(),
				baseMaxForward		= reader.ReadSingle(),
				baseForwardFriction = reader.ReadSingle(),
				boingImpactSpeed	= reader.ReadSingle(),
				boingImpactAngle	= reader.ReadSingle(),
				boingDampingRate	= reader.ReadSingle(),
				boingFrequency		= reader.ReadSingle(),
				boingAmplitude		= reader.ReadSingle()
			};
				
			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioJiggleBone>(offset, item);
			return item;
		}

		public static StudioJiggleBone[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioJiggleBone[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			int itemSize = 35 * 4;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				items[i] = LoadItem(reader, lookup, currentOffset);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
		
	public enum ProcRuleType
	{
		None				  = 0,
		AxisInterpolate		  = 1,
		QuaternionInterpolate = 2,
		AimAtBone			  = 3,
		AimAtAttachment		  = 4,
		Jiggle				  = 5
	}

	public class ProceduralRule
	{
		public ProcRuleType					   Type;
		public StudioAxisInterpolateBone	   AxisInterpolate;
		public StudioQuaternionInterpolateBone QuaternionInterpolate;
		public StudioAimAtBone				   AimAtBone;
		public StudioAimAtAttachment		   AimAtAttachment;
		public StudioJiggleBone				   JiggleBone;

		public static ProceduralRule LoadItem(BinaryReader reader, Lookup lookup)
		{
			ProceduralRule item = new ProceduralRule();
			item.Type = (ProcRuleType)reader.ReadInt32();
			switch (item.Type)
			{
				default:
				{
					reader.ReadInt32();
					break;
				}
				case ProcRuleType.AxisInterpolate:			item.AxisInterpolate		= StudioAxisInterpolateBone.LoadItem(reader, lookup, offset: reader.ReadInt32()); break;
				case ProcRuleType.QuaternionInterpolate:	item.QuaternionInterpolate	= StudioQuaternionInterpolateBone.LoadItem(reader, lookup, offset: reader.ReadInt32()); break;
				case ProcRuleType.AimAtBone:				item.AimAtBone				= StudioAimAtBone.LoadItem(reader, lookup, offset: reader.ReadInt32()); break;
				case ProcRuleType.AimAtAttachment:			item.AimAtAttachment		= StudioAimAtAttachment.LoadItem(reader, lookup, offset: reader.ReadInt32()); break;
				case ProcRuleType.Jiggle:					item.JiggleBone				= StudioJiggleBone.LoadItem(reader, lookup, offset: reader.ReadInt32()); break;
			}
			return item;
		}
	}
	

	[Flags]
	public enum StudioBoneFlags
	{
		CalculateMask			= PhysicallySimulated | PhysicsProcedural | AlwaysProcedural | ScreenAlignSphere | ScreenAlignCylinder,
		PhysicallySimulated		= 0x01,	// bone is physically simulated when physics are active
		PhysicsProcedural		= 0x02,	// procedural when physics is active
		AlwaysProcedural		= 0x04,	// bone is always procedurally animated
		ScreenAlignSphere		= 0x08,	// bone aligns to the screen, not constrained in motion.
		ScreenAlignCylinder		= 0x10,	// bone aligns to the screen, constrained by it's own axis.

		Unknown1				= 0x20,
		Unknown2				= 0x40,
		Unknown3				= 0x80,

		UsedMask				= UsedByAnything,
		UsedByAnything			= UsedByVertexMask | UsedByHitbox | UsedByAttachment | UsedByBoneMerge,
		UsedByHitbox			= 0x00000100,  // bone (or child) is used by a hit box
		UsedByAttachment		= 0x00000200,  // bone (or child) is used by an attachment point
		UsedByVertexMask		= UsedByVertexLod0 | UsedByVertexLod1 | UsedByVertexLod2 | UsedByVertexLod3 | UsedByVertexLod4 | UsedByVertexLod5 | UsedByVertexLod6 | UsedByVertexLod7,
		UsedByVertexLod0		= 0x00000400, // bone (or child) is used by the toplevel model via skinned vertex
		UsedByVertexLod1		= 0x00000800,
		UsedByVertexLod2		= 0x00001000 ,
		UsedByVertexLod3		= 0x00002000,
		UsedByVertexLod4		= 0x00004000,
		UsedByVertexLod5		= 0x00008000,
		UsedByVertexLod6		= 0x00010000,
		UsedByVertexLod7		= 0x00020000,
		UsedByBoneMerge			= 0x00040000,  // bone is available for bone merge to occur against it
		Unknown4				= 0x00080000,

		//UsedByVERTEX_AT_LOD(lod) ( UsedByVERTEX_LOD0 << (lod) )
		//UsedByANYTHING_AT_LOD(lod) ( ( UsedByANYTHING & ~UsedByVERTEX_MASK ) | UsedByVERTEX_AT_LOD(lod) )

		TypeMaskMask			= FixedAlignment | HasSaveFramePos | HasSaveFrameRot | Unknown5,
		FixedAlignment			= 0x00100000,	// bone can't spin 360 degrees, all interpolation is normalized around a fixed orientation

		HasSaveFramePos			= 0x00200000,	// Vector48
		HasSaveFrameRot			= 0x00400000,	// Quaternion64
		Unknown5				= 0x00800000,
		Unknown6				= 0x01000000,
		Unknown7				= 0x02000000,
		Unknown8				= 0x04000000,
		Unknown9				= 0x08000000,
		Unknown10				= 0x10000000,
		Unknown11				= 0x20000000,
		Unknown12				= 0x40000000

	};

	// mstudiobone_t
	public class StudioBone 
	{
		public string		   Name;
		public int			   ParentBone;			// parent bone
		public int[]		   BoneController;		// bone controller index, -1 == none (int[6])

		// default values
		public Vector3		   Position;
		public Quaternion	   Quaternion;
		public Vector3		   RadianEulerRotation;
									
		// compression scale
		public Vector3		   PositionScale;
		public Vector3		   RotationScale;

		public Matrix4x4	   PoseToBone;				// matrix3x4_t
		public Quaternion	   QuaternionAlignment;
		public StudioBoneFlags Flags;
		public ProceduralRule  ProceduralRule;
		public int			   PhysicsBone;            // index into physically simulated bone
		public string		   SurfacePropertyName;
		public Contents		   Contents;
			
		private static StudioBone LoadItem(BinaryReader reader, Lookup lookup, int version)
		{
			var startSeek = reader.BaseStream.Position;

			var item = lookup.Get<StudioBone>(startSeek);
			if (item != null)
				return item;

			item = new StudioBone
			{
				Name			    = reader.ReadNullString(startSeek + reader.ReadInt32()),
				ParentBone		    = reader.ReadInt32(),
				BoneController	    = reader.ReadInts(6),
				Position		    = reader.ReadVector3(),
				Quaternion		    = reader.ReadQuaternion(),
				RadianEulerRotation = reader.ReadVector3(), // might not be every version
				PositionScale	    = reader.ReadVector3(), // might not be every version
				RotationScale	    = reader.ReadVector3(), // might not be every version
				PoseToBone		    = reader.ReadMatrix3x4(),
				QuaternionAlignment = reader.ReadQuaternion(), // might not be every version
				Flags			    = (StudioBoneFlags)reader.ReadInt32(),
				ProceduralRule	    = ProceduralRule.LoadItem(reader, lookup),
				PhysicsBone		    = reader.ReadInt32(),
				SurfacePropertyName = reader.ReadNullString(startSeek +reader.ReadInt32()),
				Contents		    = (Contents)reader.ReadInt32()
			};

			reader.ReadInts(8); // 'Unused' int[8]

			lookup.Set<StudioBone>(startSeek, item);
			return item;
		}

		public static StudioBone[] Load(BinaryReader reader, Lookup lookup, int count, long offset, int version)
		{
			var items = new StudioBone[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			int itemSize = 216;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = LoadItem(reader, lookup, version);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// mstudiobonecontroller_t
	public class StudioBoneController
	{
		public int   bone;       // -1 == 0
		public int   type;       // X, Y, Z, XR, YR, ZR, M
		public float start;
		public float end;
		public int   rest;       // byte index value at rest
		public int   inputfield; // 0-3 user set controller, 4 mouth

		public static StudioBoneController[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioBoneController[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 56;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioBoneController>(currentOffset);
				if (items[i] != null)
					continue;
					 
				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioBoneController()
				{
					bone	   = reader.ReadInt32(),
					type	   = reader.ReadInt32(),
					start	   = reader.ReadSingle(),
					end		   = reader.ReadSingle(),
					rest	   = reader.ReadInt32(),
					inputfield = reader.ReadInt32()
					// unusued 8 ints
				};

				lookup.Set<StudioBoneController>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudiohitboxset_t
	public class StudioHitboxSet
	{
		public int	  bone;
		public int	  group;              // intersection group
		public Bounds bounds;				// bounding box
		public string hitboxname;			// offset to the name of the hitbox.
			
		public static StudioHitboxSet[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioHitboxSet[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 68;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioHitboxSet>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioHitboxSet()
				{
					bone	   = reader.ReadInt32(),
					group	   = reader.ReadInt32(),
					bounds	   = reader.ReadBounds(),
					hitboxname = reader.ReadNullString(currentOffset + reader.ReadInt32())
					// unusued 8 ints
				};

				lookup.Set<StudioHitboxSet>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudiomovement_t
	public class StudioMovement
	{
		public int	   endframe;				
		public int	   motionflags;
		public float   v0;			// velocity at start of block
		public float   v1;			// velocity at end of block
		public float   angle;		// YAW rotation at end of this blocks movement
		public Vector3 vector;		// movement vector relative to this blocks initial angle
		public Vector3 position;	// relative to start of animation???
			
		public static StudioMovement[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioMovement[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 44;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioMovement>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioMovement()
				{
					endframe	= reader.ReadInt32(),
					motionflags = reader.ReadInt32(),
					v0			= reader.ReadSingle(),
					v1			= reader.ReadSingle(),
					angle		= reader.ReadSingle(),
					vector		= reader.ReadVector3(),
					position	= reader.ReadVector3()
				};

				lookup.Set<StudioMovement>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}
		
	[Flags]
	public enum StudioAnimationFlags : byte
	{		
		RawPosition	 = 0x01, // Vector48				  - STUDIO_ANIM_RAWPOS	
		RawRotation	 = 0x02, // Quaternion48			  - STUDIO_ANIM_RAWROT	
		AnimPosition = 0x04, // mstudioanim_valueptr_t - STUDIO_ANIM_ANIMPOS	
		AnimRotation = 0x08, // mstudioanim_valueptr_t - STUDIO_ANIM_ANIMROT	
		Delta		 = 0x10, //						  - STUDIO_ANIM_DELTA	
		RawRotation2 = 0x20	// Quaternion64			  - STUDIO_ANIM_RAWROT2	
	}
		
	//mstudioanimvalue_t
	public class StudioAnimValue
	{
		public byte	 Valid;
		public byte	 Total;
		public short Value
		{
			get
			{
				unchecked { return (short)(Valid + (Total >> 8)); }
			}
			set
			{
				unchecked
				{
					Valid = (byte)value;
					Total = (byte)(value << 8);
				}
			}
		}
	};

	// mstudioanim_t
	// per bone per animation DOF and weight pointers
	public class StudioAnimation
	{
		public byte				    Bone;
		public StudioAnimationFlags Flags;      // weighing options

		public Quaternion        Rotation;
		public short[]			 AnimOffsets;
		public StudioAnimValue[] AnimValues;

		// valid for animating data only
		//inline byte				*pData( void ) const { return (((byte *)this) + sizeof( struct mstudioanim_t )); };
		//inline mstudioanim_valueptr_t	*pRotV( void ) const { return (mstudioanim_valueptr_t *)(pData()); };
		//inline mstudioanim_valueptr_t	*pPosV( void ) const { return (mstudioanim_valueptr_t *)(pData()) + ((flags & STUDIO_ANIM_ANIMROT) != 0); };

		// valid if animation unvaring over timeline
		//inline Quaternion48		*pQuat48( void ) const { return (Quaternion48 *)(pData()); };
		//inline Quaternion64		*pQuat64( void ) const { return (Quaternion64 *)(pData()); };
		//inline Vector48			*pPos( void ) const { return (Vector48 *)(pData() + ((flags & STUDIO_ANIM_RAWROT) != 0) * sizeof( *pQuat48() ) + ((flags & STUDIO_ANIM_RAWROT2) != 0) * sizeof( *pQuat64() ) ); };

		public StudioAnimation Next;
		//inline mstudioanim_t	*pNext( void ) const { if (nextoffset != 0) return  (mstudioanim_t *)(((byte *)this) + nextoffset); else return NULL; };
			
		private static StudioAnimation LoadItem(BinaryReader reader, Lookup lookup, long currentOffset, out short next)
		{
			var startSeek = reader.BaseStream.Position;
				
			var item = lookup.Get<StudioAnimation>(currentOffset);
			if (item != null)
			{
				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				reader.ReadByte();
				reader.ReadByte();
				next = reader.ReadInt16();
				return item;
			}
				
			reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);

			item = new StudioAnimation
			{
				Bone  = reader.ReadByte(),
				Flags = (StudioAnimationFlags)reader.ReadByte()
			};
			next = reader.ReadInt16();

			//inline mstudioanim_valueptr_t	*pRotV( void ) const { return (mstudioanim_valueptr_t *)(pData()); };
			//inline mstudioanim_valueptr_t	*pPosV( void ) const { return (mstudioanim_valueptr_t *)(pData()) + ((flags & STUDIO_ANIM_ANIMROT) != 0); };

			// valid if animation unvaring over timeline
			//inline Quaternion48		*pQuat48( void ) const { return (Quaternion48 *)(pData()); };
			//inline Quaternion64		*pQuat64( void ) const { return (Quaternion64 *)(pData()); };
			//inline Vector48			*pPos( void ) const { return (Vector48 *)(pData() + ((flags & STUDIO_ANIM_RAWROT) != 0) * sizeof( *pQuat48() ) + ((flags & STUDIO_ANIM_RAWROT2) != 0) * sizeof( *pQuat64() ) ); };

			if ((item.Flags & StudioAnimationFlags.RawRotation) != 0)
			{
				ushort x = reader.ReadUInt16();
				ushort y = reader.ReadUInt16();
				ushort t = reader.ReadUInt16();
				ushort z = (ushort)(t & (ushort)16384);
				bool wneg = (t > 16384);
										
				var qx = ((int)x - 32768) * (1 / 32768.0);
				var qy = ((int)y - 32768) * (1 / 32768.0);
				var qz = ((int)z - 16384) * (1 / 16384.0);
				var qw = Math.Sqrt( 1 - x * x - y * y - z * z );
				if (wneg)
					qw = -qw;
				item.Rotation = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
			} else
			if ((item.Flags & StudioAnimationFlags.RawRotation2) != 0)
			{
				UInt64 v = reader.ReadUInt64();

				UInt64 x = (v & (1<<21)); v >>= 21;
				UInt64 y = (v & (1<<21)); v >>= 21;
				UInt64 z = (v & (1<<21)); v >>= 21;
				bool wneg = (v > 0);
					
				// shift to -1048576, + 1048575, then round down slightly to -1.0 < x < 1.0
				var qx = ((int)x - 1048576) * (1 / 1048576.5f);
				var qy = ((int)y - 1048576) * (1 / 1048576.5f);
				var qz = ((int)z - 1048576) * (1 / 1048576.5f);
				var qw = Math.Sqrt( 1 - qx * qx - qy * qy - qz * qz );
				if (wneg)
					qw = -qw;
				item.Rotation = new Quaternion((float)qx, (float)qy, (float)qz, (float)qw);
			} else
			if ((item.Flags & StudioAnimationFlags.AnimRotation) != 0)
			{
				var offsets = new []
				{						
					reader.ReadInt16(),
					reader.ReadInt16(),
					reader.ReadInt16()
				};

				var startAnimSeek = reader.BaseStream.Position;
				StudioAnimValue[] animValues = new StudioAnimValue[3];
				for (int o = 0; o < 3; o++)
				{
					if (offsets[o] <= 0)
					{
						animValues[o] = null;
						continue;
					}
						
					reader.BaseStream.Seek(startAnimSeek + offsets[o], SeekOrigin.Begin);
					animValues[o] = new StudioAnimValue
					{
						Value = reader.ReadByte(),
						Total = reader.ReadByte()
					};
				}
				item.AnimOffsets = offsets;
				item.AnimValues  = animValues;
			}


			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioAnimation>(startSeek, item);
			return item;
		}

		public static StudioAnimation[] Load(BinaryReader reader, Lookup lookup, long offset, int endOffset)
		{
			//if (offset == 0)
				return new StudioAnimation[0];

			#if false
			var items = new List<StudioAnimation>();
			var startSeek = reader.BaseStream.Position;
			var currentOffset = offset;
			StudioAnimation prev = null;
			while (currentOffset < endOffset)
			{
				short next = 0;
				var item = LoadItem(reader, lookup, currentOffset, out next);
				items.Add(item);
				if (prev != null)
					prev.Next = item;
				prev = item;
				if (next == 0)
				{
					break;
				}
				currentOffset += next;
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items.ToArray();
			#endif
		}
	};
		
	[Flags]
	public enum AnimationType
	{
		None		= 0,
		Looping		= 0x0001,	// ending frame should be the same as the starting frame
		Snap		= 0x0002,	// do not interpolate between previous animation and this one
		Delta		= 0x0004,	// this sequence "adds" to the base sequences, not slerp blends
		Autoplay	= 0x0008,	// temporary flag that forces the sequence to always play
		Post		= 0x0010,	//
		Allzeros	= 0x0020,	// this animation/sequence has no real animation data
		Cyclepose	= 0x0080,	// cycle index is taken from a pose parameter index
		Realtime	= 0x0100,	// cycle index is taken from a real-time clock, not the animations cycle index
		Local		= 0x0200,	// sequence has a local context sequence
		Hidden		= 0x0400,	// don't show in default selection views
		Override	= 0x0800,	// a forward declared sequence (empty)
		Activity	= 0x1000,	// Has been updated at runtime to activity index
		Event		= 0x2000,	// Has been updated at runtime event index
		World		= 0x4000,	// sequence blends in worldspace
		FrameAnim	= 0x0040,	// animation is encoded as by frame x bone instead of RLE bone x frame
		NoForceLoop = 0x8000,	// do not force the animation loop
		EventClient = 0x10000,	// Has been updated at runtime to event index on client
	}

	// mstudioanimsections_t
	public class StudioAnimationSections
	{
		public int animblock;
		public int animindex;

		public static StudioAnimationSections[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new StudioAnimationSections[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 100;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioAnimationSections()
				{
					animblock = reader.ReadInt32(),
					animindex = reader.ReadInt32()
				};
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioanimdesc_t
	public class StudioAnimationDescription
	{
		public string name;
			
		public float fps;      // frames per second	
		public AnimationType flags;      // looping/non-looping flags

		public int numframes;

		// piecewise movement
		public StudioMovement[] movement;
			
		[NonSerialized]
		public int[] unused1;//[6];         // remove as appropriate (and zero if loading older versions)	

		public int animblock;
		public int animindex;   // non-zero when anim data isn't in sections
		//mstudioanim_t* pAnimBlock(int block, int index) const; // returns pointer to a specific anim block (local or external)
		//mstudioanim_t* pAnim(int* piFrame, float &flStall ) const; // returns pointer to data and new frame index
		//mstudioanim_t* pAnim(int* piFrame) const; // returns pointer to data and new frame index
			
		public int numikrules;
		public int ikruleindex;    // non-zero when IK data is stored in the mdl
		public int animblockikruleindex; // non-zero when IK data is stored in animblock file
		//mstudioikrule_t* pIKRule(int i) const;

		public int numlocalhierarchy;
		public int localhierarchyindex;
		//mstudiolocalhierarchy_t* pHierarchy(int i) const;


		public StudioAnimationSections[] sections;
		//public int sectionindex;
		//public int sectionframes; // number of frames used in each fast lookup section, zero if not used
		//inline mstudioanimsections_t * const pSection(int i ) const { return (mstudioanimsections_t*)(((byte*)this) + sectionindex) + i; }

		public short zeroframespan;    // frames per span
		public short zeroframecount; // number of spans
		public int zeroframeindex;
		//byte* pZeroFrameData() const { if (zeroframeindex) return (((byte*)this) + zeroframeindex); else return NULL; };
		public float zeroframestalltime;       // saved during read stalls

		// value should be between 0 & 1 inclusive
		public static float SimpleSpline( float value )
		{
			float valueSquared = value * value;

			// Nice little ease-in, ease-out spline-like curve
			return (3 * valueSquared - 2 * valueSquared * value);
		}

		public StudioAnimation pAnim(MDL header, ref int piFrame)
		{
			float flStall = 0;
			return pAnim(header, ref piFrame, out flStall);
		}
			
		public StudioAnimation pAnim(MDL header, ref int piFrame, out float flStall )
		{
			StudioAnimation panim = null;

			int block = animblock;
			int index = animindex;
			int section = 0;

			if (sections.Length != 0)
			{
				if (numframes > sections.Length && piFrame == numframes - 1)
				{
					// last frame on long anims is stored separately
					piFrame = 0;
					section = (numframes / sections.Length) + 1;
				}
				else
				{
					section = piFrame / sections.Length;
					piFrame -= section * sections.Length;
				}

				block = sections[section].animblock;
				index = sections[section].animindex;
			}

			if (block == -1)
			{
				flStall = 0.0f;
				// model needs to be recompiled
				return null;
			}

			panim = header.pAnimBlock( block, index );

			// force a preload on the next block
			if ( sections.Length != 0 )
			{
				int count = ( numframes / sections.Length) + 2;
				for ( int i = section + 1; i < count; i++ )
				{
					if ( sections[i].animblock != block )
					{
						header.pAnimBlock( sections[i].animblock, sections[i].animindex );
						break;
					}
				}
			}

			if (panim == null)
			{
				// back up until a previously loaded block is found
				while (--section >= 0)
				{
					block = sections[section].animblock;
					index = sections[section].animindex;
					panim = header.pAnimBlock( block, index );
					if (panim != null)
					{
						// set it to the last frame in the last valid section
						piFrame = sections.Length - 1;
						break;
					}
				}
			}

			// try to guess a valid stall time interval (tuned for the X360)
			flStall = 0.0f;
			if (panim == null && section <= 0)
			{
				// TODO: investigate this
				zeroframestalltime = 1;//Time.timeSinceLevelLoad;
				flStall = 1.0f;
			}
			else if (panim != null && zeroframestalltime != 0.0f)
			{
				// TODO: investigate this
				float dt = 1;// Time.timeSinceLevelLoad - zeroframestalltime;
				if (dt >= 0.0)
				{
					flStall = SimpleSpline( Mathf.Clamp( (0.200f - dt) * 5.0f, 0.0f, 1.0f ) );
				}

				if (flStall == 0.0f)
				{
					// disable stalltime
					zeroframestalltime = 0.0f;
				}
			}
			return panim;
		}

		public static StudioAnimationDescription[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioAnimationDescription[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 100;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioAnimationDescription>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				//baseptr = 
				reader.ReadInt32();
				items[i] = new StudioAnimationDescription()
				{
					name				 = reader.ReadNullString(currentOffset + reader.ReadInt32()),

					fps					 = reader.ReadSingle(),
					flags				 = (AnimationType)reader.ReadInt32(),

					numframes			 = reader.ReadInt32(),

					movement			 = StudioMovement.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32()),
						
					unused1				 = reader.ReadInts(6),
							
					animblock			 = reader.ReadInt32(),
					animindex			 = reader.ReadInt32(),

					numikrules			 = reader.ReadInt32(),
					ikruleindex			 = reader.ReadInt32(),    
					animblockikruleindex = reader.ReadInt32(), 

					numlocalhierarchy	 = reader.ReadInt32(),
					localhierarchyindex	 = reader.ReadInt32(),
			
					sections			 = StudioAnimationSections.Load(reader, offset: reader.ReadInt32(), count: reader.ReadInt32()),
					//sectionindex		 = reader.ReadInt32(),
					//sectionframes		 = reader.ReadInt32(), 

					zeroframespan		 = reader.ReadInt16(),  
					zeroframecount		 = reader.ReadInt16(), 
					zeroframeindex		 = reader.ReadInt32(),
					zeroframestalltime	 = reader.ReadSingle()
				};

				lookup.Set<StudioAnimationDescription>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioautolayer_t
	public class StudioAutoLayer
	{
		public short iSequence;
		public short iPose;
		public int   flags;
		public float start;		// beginning of influence
		public float peak;		// start of full influence
		public float tail;		// end of full influence
		public float end;		// end of all influence

		public static StudioAutoLayer[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioAutoLayer[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 24;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioAutoLayer>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioAutoLayer()
				{
					iSequence = reader.ReadInt16(), 
					iPose	  = reader.ReadInt16(),
					flags	  = reader.ReadInt32(),
					start	  = reader.ReadSingle(),   
					peak	  = reader.ReadSingle(),    
					tail	  = reader.ReadSingle(),    
					end		  = reader.ReadSingle()      
				};

				lookup.Set<StudioAutoLayer>(currentOffset, items[i]);
			}
			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioseqdesc_t
	public class StudioSequenceDescription
	{
		public static StudioSequenceDescription empty;

		public string label;

		public string activityName;

		public AnimationType flags;      // looping/non-looping flags

		public int activity;   // initialized at loadtime to game DLL values
		public int actweight;

		public int numevents;
		public int eventindex;
		//inline mstudioevent_t * pEvent(int i) const { Assert(i >= 0 && i<numevents); return (mstudioevent_t*)(((byte*)this) + eventindex) + i; };

		public Bounds bounds;       // per sequence bounding box

		public int numblends;

		// Index into array of shorts which is groupsize[0] x groupsize[1] in length
		//public int animindexindex;
		public int[,] anim;

		public int movementindex;  // [blend] float array for blended movement
		public int[] groupsize = new int[2];//[2];
		public int[] paramindex;//[2];  // X, Y, Z, XR, YR, ZR
		public float[] paramstart;//[2];    // local (0..1) starting value
		public float[] paramend;//[2];  // local (0..1) ending value
		public int paramparent;

		public float fadeintime;       // ideal cross fate in time (0.2 default)
		public float fadeouttime;  // ideal cross fade out time (0.2 default)

		public int localentrynode;     // transition node at entry
		public int localexitnode;      // transition node at exit
		public int nodeflags;      // transition rules

		public float entryphase;       // used to match entry gait
		public float exitphase;        // used to match exit gait

		public float lastframe;        // frame that should generation EndOfSequence

		public int nextseq;        // auto advancing sequences
		public int pose;           // index of delta animation between end and nextseq

		public int numikrules;

		public StudioAutoLayer[] autoLayers;

		public float[] boneWeights;
		public int weightlistindex;
		//inline float* pBoneweight(int i) const { return ((float*)(((byte*)this) + weightlistindex) + i); };
		//inline float weight(int i) const { return *(pBoneweight(i)); };

		// FIXME: make this 2D instead of 2x1D arrays
		public int posekeyindex;
		//float* pPoseKey(int iParam, int iAnim) const { return (float*)(((byte*)this) + posekeyindex) + iParam * groupsize [0] + iAnim; }
		//float poseKey(int iParam, int iAnim) const { return *(pPoseKey(iParam, iAnim )); }

		public int numiklocks;
		public int iklockindex;
		//inline mstudioiklock_t * pIKLock(int i) const { Assert(i >= 0 && i<numiklocks); return (mstudioiklock_t*)(((byte*)this) + iklockindex) + i; };

		// Key values
		public int keyvalueindex;
		public int keyvaluesize;
		//inline const char* KeyValueText(void) const { return keyvaluesize != 0 ? ((char*)this) + keyvalueindex : NULL; }

		public int cycleposeindex;     // index of pose parameter to use as cycle index

		[NonSerialized]
		public int[] unused;//[7];      // remove/add as appropriate (grow back to 8 ints on version change!)

		public static StudioSequenceDescription[] Load(BinaryReader reader, Lookup lookup, int count, long offset, int boneCount)
		{
			var items = new StudioSequenceDescription[count];
			if (count == 0 || offset == 0)
				return items;


			var startSeek = reader.BaseStream.Position;
			const int itemSize = 212;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioSequenceDescription>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				//baseptr = 
				reader.ReadInt32();
				items[i] = new StudioSequenceDescription()
				{
					label				= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					activityName		= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					flags				= (AnimationType)reader.ReadInt32(),
					activity			= reader.ReadInt32(),
					actweight			= reader.ReadInt32(),
					numevents			= reader.ReadInt32(),
					eventindex			= reader.ReadInt32(),
					bounds				= reader.ReadBounds(),
					numblends			= reader.ReadInt32()
				};
				var animindexindex			= reader.ReadInt32();
				items[i].movementindex		= reader.ReadInt32();
				items[i].groupsize			= reader.ReadInts(2);
				items[i].paramindex			= reader.ReadInts(2);
				items[i].paramstart			= reader.ReadFloats(2);
				items[i].paramend			= reader.ReadFloats(2);
				items[i].paramparent		= reader.ReadInt32();
				items[i].fadeintime			= reader.ReadSingle();
				items[i].fadeouttime		= reader.ReadSingle();
				items[i].localentrynode		= reader.ReadInt32(); 
				items[i].localexitnode		= reader.ReadInt32();
				items[i].nodeflags			= reader.ReadInt32();
				items[i].entryphase			= reader.ReadSingle();
				items[i].exitphase			= reader.ReadSingle();
				items[i].lastframe			= reader.ReadSingle();
				items[i].nextseq			= reader.ReadInt32();
				items[i].pose				= reader.ReadInt32();
				items[i].numikrules			= reader.ReadInt32();
				items[i].autoLayers			= StudioAutoLayer.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
				items[i].boneWeights		= reader.ReadFloats(offset: reader.ReadInt32(), count: boneCount);
				items[i].posekeyindex		= reader.ReadInt32();
				items[i].numiklocks			= reader.ReadInt32();
				items[i].iklockindex		= reader.ReadInt32();
				items[i].keyvalueindex		= reader.ReadInt32();
				items[i].keyvaluesize		= reader.ReadInt32();
				items[i].cycleposeindex		= reader.ReadInt32();
				items[i].unused				= reader.ReadInts(7);
					
				var sizeX = items[i].groupsize[0];
				var sizeY = items[i].groupsize[1];
				var animInts = reader.ReadInts(sizeX * sizeY, currentOffset + animindexindex);
				items[i].anim = new int[sizeY, sizeX];
				for (int ay = 0,a=0; ay < sizeY; ay++)
				{
					for (int ax = 0; ax < sizeX; ax++,a++)
					{
						items[i].anim[ay, ax] = animInts[a];
					}
				}

				lookup.Set<StudioSequenceDescription>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudiotexture_t
	[DebuggerDisplay("Name {Name}, Flags {Flags}")]
	public class StudioTexture
	{
		// Number of bytes past the beginning of this structure
		// where the first character of the texture name can be found.
		//public long		name_offset;        // Offset for null-terminated string
		public string	Name;
		public int		Flags;

		private static StudioTexture LoadItem(BinaryReader reader, Lookup lookup)
		{
			var startSeek = reader.BaseStream.Position;
				
			var item = lookup.Get<StudioTexture>(startSeek);
			if (item != null)
				return item;

			item = new StudioTexture
			{
				Name = reader.ReadNullString(startSeek + reader.ReadInt32()),
				Flags = reader.ReadInt32()
			};

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioTexture>(startSeek, item);
			return item;
		}

		public static StudioTexture[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioTexture[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 16 * 4;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = LoadItem(reader, lookup);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// mstudio_meshvertexdata_t
	public class StudioMeshVertexData
	{
		// indirection to this mesh's model's vertex data (set at runtime to data in vvd file)
		public int	ModelVertexData; //mstudio_modelvertexdata_t	*

		// used for fixup calcs when culling top level lods
		// expected number of mesh verts at desired lod
		public int[] NumLodVertices;//[ValveConstants.MAX_NUM_LODS];
	};

	// mstudiomesh_t
	public class StudioMesh
	{
		public int							MaterialIndex;

		// StudioModel
		public long							ModelOffset;        // int on disk
		[HideInInspector][NonSerialized]
		public StudioModel                  Model;

		public int							VertexCount;		// number of unique vertices/normals/texcoords
		public int							VertexIndexStart;	// vertex mstudiovertex_t

		public int							FlexCount;			// vertex animation
		public int							FlexOffset;

		// special codes for material operations
		public int							MaterialType;
		public int							MaterialParam;
			
		public int							Id;					 // a unique ordinal for this mesh

		public Vector3						Center;
			
		[HideInInspector][NonSerialized]
		public StudioMeshVertexData			VertexData;
			
		//StudioMeshVertexData GetVertexData( MdlHeader mdlHeader ) // sets vertexdata.modelvertexdata to vertex data of vvd file at runtime

		private static StudioMesh LoadItem(BinaryReader reader, Lookup lookup, StudioModel model, long modelStartSeek)
		{
			var startSeek = reader.BaseStream.Position;

			var item = lookup.Get<StudioMesh>(startSeek);
			if (item != null)
				return item;

			var materialIndex		= reader.ReadInt32();
			var modelOffset			= startSeek + reader.ReadInt32();
			if (modelOffset != modelStartSeek)
			{
				//Debug.Log("model = null");
				model = null;
			}

			item = new StudioMesh
			{
				MaterialIndex		= materialIndex,
				ModelOffset			= modelOffset,
				Model				= model,
				VertexCount			= reader.ReadInt32(),
				VertexIndexStart	= reader.ReadInt32(),
				FlexCount			= reader.ReadInt32(),
				FlexOffset			= reader.ReadInt32(),
				MaterialType		= reader.ReadInt32(),
				MaterialParam		= reader.ReadInt32(),
				Id					= reader.ReadInt32(),
				Center				= reader.ReadVector3(),
				VertexData			= new StudioMeshVertexData
				{
					ModelVertexData = reader.ReadInt32(),
					NumLodVertices	= reader.ReadInts(SourceEngineConstants.MaxNumLods)
				},
			};

			reader.ReadInts(8); // 'Unused' int[8]


			lookup.Set<StudioMesh>(startSeek, item);
			return item;
		}

		public static StudioMesh[] Load(BinaryReader reader, Lookup lookup, int count, long offset, StudioModel model, long modelStartSeek)
		{
			if (count <= 0 || count > 64 || offset == 0)
				return new StudioMesh[0];
				
			var items = new StudioMesh[count];

			var startSeek	= reader.BaseStream.Position;
			const int itemSize = 116;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = LoadItem(reader, lookup, model, modelStartSeek);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
		
	// mstudio_modelvertexdata_t
	public class StudioModelVertexData
	{
		// base of external vertex data stores
		public int VertexData;	// const void*
		public int TangentData; // const void*
	};

	// mstudiomodel_t
	public class StudioModel
	{
		public long SeekPosition;

		public string Name; // 64 bytes

		public int Type;

		public float BoundingRadius;

		public StudioMesh[] Meshes;

		// cache purposes
		public int VertexCount;     // number of unique vertices/normals/texcoords
		public int VertexIndex;     // vertex Vector
		public int TangentsIndex;   // tangents Vector

		// These functions are defined in application-specific code:
		public int NumAttachments;
		public int AttachmentIndex;

		public int NumEyeballs;
		public int EyeballIndex;

		//public StudioModelVertexData Vertexdata; // set at runtime as vertices/tangents of vvd file
			
		private static StudioModel LoadItem(BinaryReader reader, Lookup lookup)
		{
			var startSeek = reader.BaseStream.Position;

			var item = lookup.Get<StudioModel>(startSeek);
			if (item != null)
				return item;

			item = new StudioModel
			{
				SeekPosition	= startSeek,				 
				Name			= reader.ReadStringWithLength(64),
				Type			= reader.ReadInt32(),
				BoundingRadius	= reader.ReadSingle()
			};

			item.Meshes			= StudioMesh.Load(reader, lookup, reader.ReadInt32(), startSeek + reader.ReadInt32(), item, startSeek);

			// cache purposes
			item.VertexCount		= reader.ReadInt32();	// number of unique vertices/normals/texcoords			
			item.VertexIndex		= reader.ReadInt32();	// vertex Vector
			item.TangentsIndex		= reader.ReadInt32();	// tangents Vector

			// These functions are defined in application-specific code:
			item.NumAttachments		= reader.ReadInt32();
			item.AttachmentIndex	= reader.ReadInt32();

			item.NumEyeballs		= reader.ReadInt32();
			item.EyeballIndex		= reader.ReadInt32();

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);

			lookup.Set<StudioModel>(startSeek, item);
			return item;
		}

		public static StudioModel[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			if (count <= 0 || count > 64 || offset == 0)
				return new StudioModel[0];
								
			var items = new StudioModel[count];

			var startSeek = reader.BaseStream.Position;

			const int itemSize = 124;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = LoadItem(reader, lookup);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// mstudiobodyparts_t
	public class StudioBodyparts
	{
		public string			Name;
		public int				Base;
		public StudioModel[]	Models;

		private static StudioBodyparts LoadItem(BinaryReader reader, Lookup lookup)
		{
			var startSeek			= reader.BaseStream.Position;

			var item = lookup.Get<StudioBodyparts>(startSeek);
			if (item != null)
				return item;

			item			= new StudioBodyparts();
			item.Name		= reader.ReadNullString(startSeek + reader.ReadInt32());
			var numModels	= reader.ReadInt32();
			item.Base		= reader.ReadInt32();
			item.Models		= StudioModel.Load(reader, lookup, numModels, startSeek + reader.ReadInt32());

			lookup.Set<StudioBodyparts>(startSeek, item);
			return item;
		}

		public static StudioBodyparts[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioBodyparts[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 16;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = LoadItem(reader, lookup);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// mstudioattachment_t
	public class StudioAttachment
	{
		public string		name;
		public uint			flags;
		public int			localbone;
		public Matrix4x4	local; // attachment point
		public static StudioAttachment[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioAttachment[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 92;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioAttachment>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioAttachment()
				{
					name		= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					flags		= reader.ReadUInt32(),
					localbone	= reader.ReadInt32(),
					local		= reader.ReadMatrix3x4()
					//unused	= reader.ReadInts(8)
				};

				lookup.Set<StudioAttachment>(currentOffset, items[i]);
			}
			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioflexdesc_t
	public class StudioFlexDescription
	{
		public string FACS; // name of 'bones'?

		public static StudioFlexDescription[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioFlexDescription[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 4;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioFlexDescription>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioFlexDescription()
				{
					FACS = reader.ReadNullString(currentOffset + reader.ReadInt32()),
				};

				lookup.Set<StudioFlexDescription>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioflexcontroller_t
	public class StudioFlexController
	{
		public string	type;
		public string	name;
		public int		localToGlobal;  // remapped at load time to master list
		public float	min;
		public float	max;

		public static StudioFlexController[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioFlexController[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 20;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioFlexController>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioFlexController()
				{
					type			= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					name			= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					localToGlobal	= reader.ReadInt32(),
					min				= reader.ReadSingle(),
					max				= reader.ReadSingle()
				};

				lookup.Set<StudioFlexController>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioflexop_t
	public class StudioFlexOp
	{
		public int		op;
		//union 
		//{
			public int		index;
		//	float	value;
		//} d;
			
		public static StudioFlexOp Load(BinaryReader reader, Lookup lookup, long offset)
		{
			if (offset == 0)
				return null;

			var startSeek = reader.BaseStream.Position;

			reader.BaseStream.Seek(offset, SeekOrigin.Begin);
			var item = new StudioFlexOp()
			{
				op		= reader.ReadInt32(),
				index	= reader.ReadInt32()
			};

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return item;
		}
	}

	// mstudioflexrule_t
	public class StudioFlexRule
	{
		public int	flex;
		public int	numops;
		public StudioFlexOp op;

		public static StudioFlexRule[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioFlexRule[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 12;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioFlexRule>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioFlexRule()
				{
					flex	= reader.ReadInt32(),
					numops	= reader.ReadInt32(),
					op		= StudioFlexOp.Load(reader, lookup, reader.ReadInt32())
				};

				lookup.Set<StudioFlexRule>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioiklink_t
	public class StudioInverseKinematicsLink
	{
		public int		bone;
		public Vector3	kneeDir; // ideal bending direction (per link, if applicable)

		[NonSerialized]
		public Vector3	unused0; // unused

		public static StudioInverseKinematicsLink Load(BinaryReader reader, Lookup lookup, long offset)
		{
			if (offset == 0)
				return null;

			var startSeek = reader.BaseStream.Position;

			reader.BaseStream.Seek(offset, SeekOrigin.Begin);
			var item = new StudioInverseKinematicsLink()
			{
				bone	= reader.ReadInt32(),
				kneeDir = reader.ReadVector3(),
				unused0 = reader.ReadVector3()
			};
				
			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return item;
		}
	}

	// mstudioikchain_t
	public class StudioInverseKinematicsChain
	{
		public string 	name;
		public int		linktype;
		public int		numlinks;
		public StudioInverseKinematicsLink link;

		public static StudioInverseKinematicsChain[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioInverseKinematicsChain[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 16;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioInverseKinematicsChain>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioInverseKinematicsChain()
				{
					name		= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					linktype	= reader.ReadInt32(),
					numlinks	= reader.ReadInt32(),
					link		= StudioInverseKinematicsLink.Load(reader, lookup, reader.ReadInt32())
				};

				lookup.Set<StudioInverseKinematicsChain>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudiomouth_t
	public class StudioMouth
	{
		public int		bone;
		public Vector3 forward;
		public int		flexdesc;

		public static StudioMouth[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioMouth[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 24;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioMouth>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioMouth()
				{
					bone		= reader.ReadInt32(),
					forward		= reader.ReadVector3(),
					flexdesc	= reader.ReadInt32()
				};

				lookup.Set<StudioMouth>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioposeparamdesc_t
	public class StudioPoseParamDescription
	{
		public string	name;
		public int		flags;  // ????
		public float	start;	// starting value
		public float	end;	// ending value
		public float	loop;	// looping range, 0 for no looping, 360 for rotations, etc.

		public static StudioPoseParamDescription[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioPoseParamDescription[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 20;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioPoseParamDescription>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioPoseParamDescription()
				{
					name		= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					flags		= reader.ReadInt32(),
					start		= reader.ReadSingle(),
					end			= reader.ReadSingle(),
					loop		= reader.ReadSingle()
				};
				//Debug.Log(items[i].name);

				lookup.Set<StudioPoseParamDescription>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioiklock_t
	public class StudioInverseKinematicLock
	{
		public int		chain;
		public float	posWeight;
		public float	localQuaternionWeight;
		public int		flags;

		public static StudioInverseKinematicLock[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioInverseKinematicLock[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 32;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioInverseKinematicLock>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioInverseKinematicLock()
				{
					chain					= reader.ReadInt32(),
					posWeight				= reader.ReadSingle(),
					localQuaternionWeight	= reader.ReadSingle(),
					flags					= reader.ReadInt32(),
					// 4 ints unused
				};

				lookup.Set<StudioInverseKinematicLock>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudiomodelgroup_t
	public class StudioModelGroup
	{
		public string label;
		public string name; // = other model file ..
		public MDL header;

		public static StudioModelGroup[] Load(BinaryReader reader, GameResources gameResources, Lookup lookup, int count, long offset)
		{
			var items = new StudioModelGroup[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 8;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioModelGroup>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioModelGroup()
				{
					label	= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					name	= reader.ReadNullString(currentOffset + reader.ReadInt32())
				};

				var mdlEntry = gameResources.GetEntry(items[i].name);
				if (mdlEntry != null)
				{
					items[i].header = gameResources.LoadMDL(mdlEntry, lookup);
				}
				else
					items[i].header = null;

				lookup.Set<StudioModelGroup>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioanimblock_t
	public class StudioAnimationBlock
	{
		public StudioAnimation[] Animations;
		//public int datastart;
		//public int dataend;

		public static StudioAnimationBlock[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioAnimationBlock[count];
			if (count == 0 || offset == 0)
				return items;
				
			var startSeek = reader.BaseStream.Position;
			const int itemSize = 8;

			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioAnimationBlock>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
					
				var dataStart	= reader.ReadInt32();
				var dataEnd		= reader.ReadInt32();

				items[i] = new StudioAnimationBlock
				{
					Animations = StudioAnimation.Load(reader, lookup, dataStart, dataEnd)
				};

				lookup.Set<StudioAnimationBlock>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	// mstudioflexcontrollerui_t
	public class StudioFlexControllerUi
	{
		public string		name; // bone names?
			
		// These are used like a union to save space
		// Here are the possible configurations for a UI controller
		//
		// SIMPLE NON-STEREO:	0: control	1: unused	2: unused
		// STEREO:				0: left		1: right	2: unused
		// NWAY NON-STEREO:		0: control	1: unused	2: value
		// NWAY STEREO:			0: left		1: right	2: value

		public int			szindex0;
		public int			szindex1;
		public int			szindex2;

		// inline const mstudioflexcontroller_t *pController( void ) const { return !stereo ? (mstudioflexcontroller_t *)( (char *)this + szindex0 ) : NULL; }
		// inline char * const	pszControllerName( void ) const { return !stereo ? pController().pszName() : NULL; }
		// inline int			controllerIndex( const CStudioHdr &cStudioHdr ) const;

		// inline const mstudioflexcontroller_t *pLeftController( void ) const { return stereo ? (mstudioflexcontroller_t *)( (char *)this + szindex0 ) : NULL; }
		// inline char * const	pszLeftName( void ) const { return stereo ? pLeftController().pszName() : NULL; }
		// inline int			leftIndex( const CStudioHdr &cStudioHdr ) const;

		// inline const mstudioflexcontroller_t *pRightController( void ) const { return stereo ? (mstudioflexcontroller_t *)( (char *)this + szindex1 ): NULL; }
		// inline char * const	pszRightName( void ) const { return stereo ? pRightController().pszName() : NULL; }
		// inline int			rightIndex( const CStudioHdr &cStudioHdr ) const;

		// inline const mstudioflexcontroller_t *pNWayValueController( void ) const { return remaptype == FLEXCONTROLLER_REMAP_NWAY ? (mstudioflexcontroller_t *)( (char *)this + szindex2 ) : NULL; }
		// inline char * const	pszNWayValueName( void ) const { return remaptype == FLEXCONTROLLER_REMAP_NWAY ? pNWayValueController().pszName() : NULL; }
		// inline int			nWayValueIndex( const CStudioHdr &cStudioHdr ) const;

		// Number of controllers this ui description contains, 1, 2 or 3
		// inline int			Count() const { return ( stereo ? 2 : 1 ) + ( remaptype == FLEXCONTROLLER_REMAP_NWAY ? 1 : 0 ); }
		// inline const mstudioflexcontroller_t *pController( int index ) const;

		public byte remaptype;  // See the FlexControllerRemapType_t enum
		public bool stereo;     // Is this a stereo control?

		public static StudioFlexControllerUi[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioFlexControllerUi[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 20;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);

				items[i] = lookup.Get<StudioFlexControllerUi>(currentOffset);
				if (items[i] != null)
					continue;

				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = new StudioFlexControllerUi()
				{
					name			= reader.ReadNullString(currentOffset + reader.ReadInt32()),
					szindex0		= reader.ReadInt32(),
					szindex1		= reader.ReadInt32(),
					szindex2		= reader.ReadInt32(),
					remaptype		= reader.ReadByte(),
					stereo			= reader.ReadByte() != 0
					//2 unused bytes
				};


				lookup.Set<StudioFlexControllerUi>(currentOffset, items[i]);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	}

	//mstudiosrcbonetransform_t
	public class StudioSourceBoneTransform
	{
		public string		name;
		public Matrix4x4	pretransform;	
		public Matrix4x4	posttransform;		

		private static StudioSourceBoneTransform LoadItem(BinaryReader reader, Lookup lookup)
		{
			var startSeek			= reader.BaseStream.Position;

			var item = lookup.Get<StudioSourceBoneTransform>(startSeek);
			if (item != null)
				return item;

			item			= new StudioSourceBoneTransform
			{
				name			= reader.ReadNullString(startSeek + reader.ReadInt32()),
				pretransform	= reader.ReadMatrix3x4(),
				posttransform	= reader.ReadMatrix3x4()
			};

			//Debug.Log(item.name);

			lookup.Set<StudioSourceBoneTransform>(startSeek, item);
			return item;
		}

		public static StudioSourceBoneTransform[] Load(BinaryReader reader, Lookup lookup, int count, long offset)
		{
			var items = new StudioSourceBoneTransform[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 100;
			for (var i = 0; i < count; i++)
			{
				var currentOffset = offset + (i * itemSize);
				reader.BaseStream.Seek(currentOffset, SeekOrigin.Begin);
				items[i] = LoadItem(reader, lookup);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}

	};
		
	[Flags]
	public enum Studioflags : int
	{
		AutogeneratedHitbox			= 1 << 0,
		UsesEnvCubemap				= 1 << 1,
		ForceOpaque					= 1 << 2,
		TranslucentTwopass			= 1 << 3,
		StaticProp					= 1 << 4,
		UsesFbTexture				= 1 << 5,   // This flag is set at loadtime, not mdl build time so that we don't have to rebuild models when we change materials.
		HasShadowLod				= 1 << 6,
		UsesBumpmapping				= 1 << 7,
		UseShadowLodMaterials		= 1 << 8,
		Obsolete					= 1 << 9,
		Unused						= 1 << 10,
		NoForcedFade				= 1 << 11,
		ForcePhonemeCrossfade		= 1 << 12,
		ConstantDirectionalLightDot = 1 << 13,
		FlexesConverted				= 1 << 14,  // Flag to mark delta flexes as already converted from disk format to memory format
		BuiltInPreviewMode			= 1 << 15,
		AmbientBoost				= 1 << 16,
		DoNotCastShadows			= 1 << 17,
		CastTextureShadows			= 1 << 18,
		SubdivisionSurface			= 1 << 19,
		NoAnimEvents				= 1 << 20,
		VertAnimFixedPointScale		= 1 << 21,
		Unknown1					= 1 << 22,
		Unknown2					= 1 << 23,
		Unknown3					= 1 << 24,
		Unknown4					= 1 << 25,
		Unknown5					= 1 << 26,
		Unknown6					= 1 << 27,
		Unknown7					= 1 << 28,
		Unknown8					= 1 << 29,
		Unknown9					= 1 << 30,
		Unknown10					= 1 << 31
	}
	
	[Flags]
	public enum Contents
	{
		EMPTY					= 0,		// No contents

		SOLID					= 0x1,		// an eye is never valid in a solid
		WINDOW					= 0x2,		// translucent, but not watery (glass)
		AUX						= 0x4,
		GRATE					= 0x8,		// alpha-tested "grate" textures.  Bullets/sight pass through, but solids don't
		SLIME					= 0x10,
		WATER					= 0x20,
		BLOCKLOS				= 0x40,		// block AI line of sight
		OPAQUE					= 0x80,		// things that cannot be seen through (may be non-solid though)
		LAST_VISIBLE_CONTENTS	= OPAQUE,

		ALL_VISIBLE_CONTENTS	= (LAST_VISIBLE_CONTENTS | (LAST_VISIBLE_CONTENTS-1)),

		TESTFOGVOLUME			= 0x100,
		UNUSED					= 0x200,

		// unused 
		// NOTE: If it's visible, grab from the top + update LAST_VISIBLE_CONTENTS
		// if not visible, then grab from the bottom.
		UNUSED6					= 0x400,

		TEAM1					= 0x800,	// per team contents used to differentiate collisions 
		TEAM2					= 0x1000,	// between players and objects on different teams

		// ignore OPAQUE on surfaces that have SURF_NODRAW
		IGNORE_NODRAW_OPAQUE	= 0x2000,

		// hits entities which are MOVETYPE_PUSH (doors, plats, etc.)
		MOVEABLE				= 0x4000,

		// remaining contents are non-visible, and don't eat brushes
		AREAPORTAL				= 0x8000,

		PLAYERCLIP				= 0x10000,
		MONSTERCLIP				= 0x20000,

		// currents can be added to any other contents, and may be mixed
		CURRENT_0				= 0x40000,
		CURRENT_90				= 0x80000,
		CURRENT_180				= 0x100000,
		CURRENT_270				= 0x200000,
		CURRENT_UP				= 0x400000,
		CURRENT_DOWN			= 0x800000,

		ORIGIN					= 0x1000000,	// removed before bsping an entity

		MONSTER					= 0x2000000,	// should never be on a brush, only in game
		DEBRIS					= 0x4000000,
		DETAIL					= 0x8000000,	// brushes to be added after vis leafs
		TRANSLUCENT				= 0x10000000,	// auto set if any surface has trans
		LADDER					= 0x20000000,
		HITBOX					= 0x40000000	// use accurate hitboxes on trace
	}

	// studiohdr_t
	public class MDL
	{
		public string							Id;				// Model format ID, such as "IDST" (0x49 0x44 0x53 0x54)
		public int								Version;		// Format version number, such as 48 (0x30,0x00,0x00,0x00)
		public int								Checksum;		// this has to be the same in the phy and vtx files to load!
		public string							Name;			// The internal name of the model, padding with null bytes. 64 bytes
																// Typically "my_model.mdl" will have an internal name of "my_model"
		public int								DataLength;		// Data size of MDL file in bytes.
			
		public Vector3							Eyeposition;	// Position of player viewpoint relative to model origin
		public Vector3							Illumposition;	// ?? Presumably the point used for lighting when per-vertex lighting is not enabled.
		public Bounds							Hull;
		public Bounds							ViewBounds;

		public Studioflags						Flags;			// Binary flags in little-endian order. 
																// ex (00000001,00000000,00000000,11000000) means flags for position 0, 30, and 31 are set. 
																// Set model flags section for more information
																
		public StudioBone[]						Bones;
		public StudioBoneController[]			BoneControllers;
		public StudioHitboxSet[]				HitboxSets;
		public StudioAnimationDescription[]		LocalAnimationDescriptions;
		public StudioSequenceDescription[]		LocalSequences;

		public int								ActivityListVersion;		// initialization flag - have the sequences been indexed?
		public int								EventsIndexed;				// ??

		public StudioTexture[]					TextureFilenames;           // VMT texture filenames
		public string[]							TextureDirs;

		// Each skin-family assigns a texture-id to a skin location
		public short[][]						SkinReferenceTable;

		public StudioBodyparts[]				Bodyparts;
		public StudioAttachment[]				AttachmentPoints;

		// Node values appear to be single bytes, while their names are null-terminated strings.
		public int								LocalNodeCount;
		public int								LocalNodeIndex;
		public int								LocalNodeNameIndex;

		public StudioFlexDescription[]			FlexDescriptions;
		public StudioFlexController[]			FlexControllers;
		public StudioFlexRule[]					FlexRules;
		public StudioInverseKinematicsChain[]	InverseKinematicsChains;
		public StudioMouth[]					Mouths;
		public StudioPoseParamDescription[]		PoseParamDescriptions;
			
		public string							SurfaceProp;       // Surface property value (single null-terminated string)
			
		[Multiline(8)]
		public string							KeyValues;
			
		public StudioInverseKinematicLock[]		InverseKinematicsLocks;


		public float							Mass;					// Mass of object (4-bytes)
		public Contents							Contents;				// ??
			
		public StudioModelGroup[]				IncludeModels;			// Other models can be referenced for re-used sequences and animations,  (See also: The $includemodel QC option.)

		[NonSerialized]
		internal int							VirtualModel;			// Placeholder for mutable-void*
			
		public string							AnimblocksName;
		public StudioAnimationBlock[]			Animblocks;
			
		public StudioAnimation pAnimBlock(int block, int index )
		{
			if (block == -1)
			{
				return null;
			}

			if (Animblocks == null ||
				Animblocks.Length == 0 ||
				block >= Animblocks.Length ||
				block < 0 ||
				index < 0)
				return null;

			var animBlock = Animblocks[block];
			if (animBlock.Animations == null || 
				index >= animBlock.Animations.Length)
				return null;

			//StudioAnimation
			return Animblocks[block].Animations[index];
		}

		[NonSerialized]
		internal int							AnimblockModel;			// Placeholder for mutable-void*
		[NonSerialized]
		internal int							BoneTableNameIndex;     // Points to a series of bytes?

		[NonSerialized]
		internal int							VertexBase;             // Placeholder for void*
		[NonSerialized]
		internal int							OffsetBase;				// Placeholder for void*

		[NonSerialized]
		public byte								DirectionalDotProduct;  // Used with $constantdirectionallight from the QC, Model should have flag #13 set if enabled

		[NonSerialized]
		public byte								RootLod;				// Preferred rather than clamped
		[NonSerialized]
		public byte								NumAllowedRootLods;     // 0 means any allowed, N means Lod 0 . (N-1)

		[NonSerialized]
		internal byte							Unused1;                // ??
		[NonSerialized]
		internal int							Unused2;				// ??
			
		public StudioFlexControllerUi[]			FlexControllerUi;
			
		[NonSerialized]
		public float							FloatVertAnimFixedPointScale;
		public float VertAnimFixedPointScale { get { return (Flags & Studioflags.VertAnimFixedPointScale) > 0 ? ((FloatVertAnimFixedPointScale != 0.0f) ? FloatVertAnimFixedPointScale : 1.0f) : 1.0f; } } // / 4096.0f

		[NonSerialized]
		internal int							Unused3;

		[NonSerialized]
		internal int                            studiohdr2index;

		[NonSerialized]
		internal int                            unused4;


		// studiohdr2_t

		public StudioSourceBoneTransform[]      SourceBoneTransforms;

		public int	IllumpositionAttachmentIndex;
		//inline int			IllumPositionAttachmentIndex() const { return illumpositionattachmentindex; }

		public float MaxEyeDeflection;
		//inline float		MaxEyeDeflection() const { return flMaxEyeDeflection != 0.0f ? flMaxEyeDeflection : 0.866f; } // default to cos(30) if not set

		public int LinearBoneIndex;
		//inline mstudiolinearbone_t *pLinearBones() const { return (linearboneindex) ? (mstudiolinearbone_t *)(((byte *)this) + linearboneindex) : NULL; }

		public string Name2;
		//inline char *pszName() { return (sznameindex) ? (char *)(((byte *)this) + sznameindex ) : NULL; }

		public int BoneFlexDriverCount; 
		public int BoneFlexDriverIndex;
		//inline mstudioboneflexdriver_t *pBoneFlexDriver( int i ) const { Assert( i >= 0 && i < m_nBoneFlexDriverCount ); return (mstudioboneflexdriver_t *)(((byte *)this) + m_nBoneFlexDriverIndex) + i; }
			
		[NonSerialized]
		public int[] reserved = new int[56];

		private static string[] LoadTextureDirs(BinaryReader reader, int count, int offset)
		{
			if (count <= 0)
				return null;

			var startSeek = reader.BaseStream.Position;

			reader.BaseStream.Seek(offset, SeekOrigin.Begin);
			var offsets = reader.ReadInts(count);

			var materialSearchPaths = new string[offsets.Length];
			for (var i = 0; i < offsets.Length; i++)
			{
				reader.BaseStream.Seek(offsets[i], SeekOrigin.Begin);
				materialSearchPaths[i] = reader.ReadNullString();
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return materialSearchPaths;
		}

		public static MDL Read(BinaryReader reader, GameResources gameResources, Lookup lookup)
		{
			var id		= reader.ReadStringWithLength(4);
			var version = reader.ReadInt32();
			var header = new MDL
			{ 
				Id								= id,
				Version							= version,
				Checksum						= reader.ReadInt32(),
				Name							= reader.ReadStringWithLength(64),
				DataLength						= reader.ReadInt32(),
				Eyeposition						= reader.ReadVector3(),
				Illumposition					= reader.ReadVector3(),
				Hull							= reader.ReadBounds(),
				ViewBounds						= reader.ReadBounds(),
				Flags							= (Studioflags)reader.ReadInt32(),

				Bones							= StudioBone				.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32(), version: version),
				BoneControllers					= StudioBoneController		.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32()),
				HitboxSets						= StudioHitboxSet			.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32()),
				LocalAnimationDescriptions		= StudioAnimationDescription.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32())
			};

			header.LocalSequences				= StudioSequenceDescription .Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32(), boneCount: header.Bones.Length);

			header.ActivityListVersion			= reader.ReadInt32();
			header.EventsIndexed				= reader.ReadInt32();

			header.TextureFilenames				= StudioTexture.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
			header.TextureDirs					= LoadTextureDirs(reader, count: reader.ReadInt32(), offset: reader.ReadInt32());
					
			header.SkinReferenceTable			= reader.ReadInt16Table(count2: reader.ReadInt32(), // 'SkinReferenceCount'
																		count1: reader.ReadInt32(), // 'SkinReferenceFamilyCount'
																		offset: reader.ReadInt32());// 'SkinReferenceIndex'

			header.Bodyparts					= StudioBodyparts.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
			header.AttachmentPoints				= StudioAttachment.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());

			header.LocalNodeCount				= reader.ReadInt32();
			header.LocalNodeIndex				= reader.ReadInt32();
			header.LocalNodeNameIndex			= reader.ReadInt32();

			header.FlexDescriptions				= StudioFlexDescription		  .Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
			header.FlexControllers				= StudioFlexController		  .Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
			header.FlexRules					= StudioFlexRule			  .Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
			header.InverseKinematicsChains		= StudioInverseKinematicsChain.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
			header.Mouths						= StudioMouth				  .Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
			header.PoseParamDescriptions		= StudioPoseParamDescription  .Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());

			header.SurfaceProp					= reader.ReadNullString(reader.ReadInt32());

			header.KeyValues					= reader.ReadStringWithLength(offset: reader.ReadInt32(), count: reader.ReadInt32());

			header.InverseKinematicsLocks		= StudioInverseKinematicLock.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
				
			header.Mass							= reader.ReadSingle();
			header.Contents						= (Contents)reader.ReadInt32();

			header.IncludeModels				= StudioModelGroup.Load(reader, gameResources, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());

			header.VirtualModel					= reader.ReadInt32();
				
			header.AnimblocksName				= reader.ReadNullString(offset: reader.ReadInt32());
			header.Animblocks					= StudioAnimationBlock.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());

			header.AnimblockModel				= reader.ReadInt32();
			header.BoneTableNameIndex			= reader.ReadInt32();

			header.VertexBase					= reader.ReadInt32();
			header.OffsetBase					= reader.ReadInt32();
				
			header.DirectionalDotProduct		= reader.ReadByte();

			header.RootLod						= reader.ReadByte();
			header.NumAllowedRootLods			= reader.ReadByte();

			header.Unused1						= reader.ReadByte();
			header.Unused2						= reader.ReadInt32();
				
			header.FlexControllerUi				= StudioFlexControllerUi.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());

			header.FloatVertAnimFixedPointScale	= reader.ReadSingle();
			header.Unused3						= reader.ReadInt32();
			header.studiohdr2index				= reader.ReadInt32();
			header.unused4						= reader.ReadInt32();

			if (header.studiohdr2index != 0)
			{
				var startSeek = reader.BaseStream.Position;
				reader.BaseStream.Seek(header.studiohdr2index, SeekOrigin.Begin);

				header.SourceBoneTransforms			= StudioSourceBoneTransform.Load(reader, lookup, count: reader.ReadInt32(), offset: reader.ReadInt32());
				header.IllumpositionAttachmentIndex = reader.ReadInt32();
				header.MaxEyeDeflection				= reader.ReadSingle();
				header.LinearBoneIndex				= reader.ReadInt32();
				header.Name2						= reader.ReadNullString(offset: reader.ReadInt32());
				header.BoneFlexDriverCount			= reader.ReadInt32();
				header.BoneFlexDriverIndex			= reader.ReadInt32();
					
				reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			} else
			{
				header.SourceBoneTransforms			= new StudioSourceBoneTransform[0];
			}
			
			return header;
		}

	};
}