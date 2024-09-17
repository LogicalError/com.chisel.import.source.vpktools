using System;
using System.IO;
using DebuggerDisplayAttribute = System.Diagnostics.DebuggerDisplayAttribute;

namespace Chisel.Import.Source.VPKTools
{
	public enum StripFlags
	{
		STRIP_IS_TRILIST = 0x01,
		STRIP_IS_TRISTRIP = 0x02,
	}
		
	public class BoneStateChangeHeader // BoneStateChangeHeader_t
	{
		public int HardwareID;
		public int NewBoneID;

		private static BoneStateChangeHeader LoadItem(BinaryReader reader)
		{
			return new BoneStateChangeHeader
			{
				HardwareID = reader.ReadInt32(),
				NewBoneID  = reader.ReadInt32()
			};
		}
			
		public static BoneStateChangeHeader[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new BoneStateChangeHeader[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);
			for (var i = 0; i < count; i++)
			{
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// a strip is a piece of a stripgroup that is divided by bones 
	// (and potentially tristrips if we remove some degenerates.)
	public class StripHeader // StripHeader_t
	{
		// indexOffset offsets into the mesh's index array.
		public int  IndexCount;
		public int  IndexMeshOffset;

		// vertexOffset offsets into the mesh's vert array.
		public int  VertexCount;
		public int  VertexMeshIndex;

		// use this to enable/disable skinning.  
		// May decide (in optimize.cpp) to put all with 1 bone in a different strip 
		// than those that need skinning.
		public short BoneCount;

		public StripFlags Flags;

		public BoneStateChangeHeader[] BoneStateChangeHeaders;

		private static StripHeader LoadItem(BinaryReader reader)
		{
			return new StripHeader
			{ 
				// indexOffset offsets into the mesh's index array.
				IndexCount      = reader.ReadInt32(),
				IndexMeshOffset = reader.ReadInt32(),
					
				// vertexOffset offsets into the mesh's vert array.
				VertexCount     = reader.ReadInt32(),
				VertexMeshIndex = reader.ReadInt32(),

				// use this to enable/disable skinning.  
				// May decide (in optimize.cpp) to put all with 1 bone in a different strip 
				// than those that need skinning.
				BoneCount = reader.ReadInt16(),

				Flags     = (StripFlags)reader.ReadByte(),

				BoneStateChangeHeaders = BoneStateChangeHeader.Load(reader, count: reader.ReadInt32(), offset: reader.ReadInt32())
			};
		}
			
		public static StripHeader[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new StripHeader[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);
			for (var i = 0; i < count; i++)
			{
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
		
	public class VertexOffset // Vertex_t
	{
		// these index into the mesh's vert[origMeshVertID]'s bones
		public byte[] BoneWeightIndex;//[MAX_NUM_BONES_PER_VERT];
		public byte   NumBones;

		public ushort OriginalMeshVertexIndex;

		// for sw skinned verts, these are indices into the global list of bones
		// for hw skinned verts, these are hardware bone indices
		public byte[] BoneId;//[MAX_NUM_BONES_PER_VERT];

		private static VertexOffset LoadItem(BinaryReader reader)
		{
			return new VertexOffset
			{
				BoneWeightIndex			= reader.ReadBytes(SourceEngineConstants.MaxNumBonesPerVert),
				NumBones				= reader.ReadByte(),
				OriginalMeshVertexIndex = reader.ReadUInt16(),
				BoneId					= reader.ReadBytes(SourceEngineConstants.MaxNumBonesPerVert)
			};
		}
			
		public static VertexOffset[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new VertexOffset[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			reader.BaseStream.Seek(offset, SeekOrigin.Begin);
			for (var i = 0; i < count; i++)
			{
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
		
	[Flags]
	public enum StripGroupFlags
	{
		STRIPGROUP_IS_FLEXED = 0x01,
		STRIPGROUP_IS_HWSKINNED	= 0x02,
		STRIPGROUP_IS_DELTA_FIXED = 0x04,
		STRIPGROUP_SUPPRESS_HW_MORPH = 0x08,
	}
		
	public class StripGroupHeader // StripGroupHeader_t
	{
		// These are the arrays of all verts and indices for this mesh.  strips index into this.
		public VertexOffset[]  VertexOffsets;
		public ushort[]		   Indices;
		public StripHeader[]   StripHeaders; // used for software skinning?
		public StripGroupFlags Flags;
			
		private static StripGroupHeader LoadItem(BinaryReader reader)
		{
			var startSeek = reader.BaseStream.Position;
			return new StripGroupHeader
			{
				VertexOffsets	= VertexOffset.Load(reader, count: reader.ReadInt32(), offset: startSeek + reader.ReadInt32()),
				Indices			= reader.ReadUShorts(count: reader.ReadInt32(), offset: startSeek + reader.ReadInt32()),
				StripHeaders	= StripHeader.Load(reader, count: reader.ReadInt32(), offset: startSeek + reader.ReadInt32()),
				Flags			= (StripGroupFlags)reader.ReadByte()
			};
		}
			
		public static StripGroupHeader[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new StripGroupHeader[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 25;
			for (var i = 0; i < count; i++)
			{
				reader.BaseStream.Seek(offset + (i * itemSize), SeekOrigin.Begin);
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}

	};
		
	public enum OptimizedModel
	{
		TriangleList  = 0x01,
		TriangleStrip = 0x02
	};
		
	public enum MeshFlags
	{
		MESH_IS_TEETH = 0x01,
		MESH_IS_EYES  = 0x02,
	}

	// a collection of locking groups:
	// up to 4:
	// non-flexed, hardware skinned
	// flexed, hardware skinned
	// non-flexed, software skinned
	// flexed, software skinned
	//
	// A mesh has a material associated with it.
	public class MeshHeader // MeshHeader_t
	{
		public StripGroupHeader[] StripGroupHeaders;
		public MeshFlags          Flags;
			
		private static MeshHeader LoadItem(BinaryReader reader)
		{
			var startSeek = reader.BaseStream.Position;
			return new MeshHeader
			{
				StripGroupHeaders = StripGroupHeader.Load(reader, count: reader.ReadInt32(), offset: startSeek + reader.ReadInt32()),
				Flags		      = (MeshFlags)reader.ReadByte()
			};
		}

		public static MeshHeader[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new MeshHeader[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 9;
			for (var i = 0; i < count; i++)
			{
				reader.BaseStream.Seek(offset + (i * itemSize), SeekOrigin.Begin);
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
		
	public class ModelLodHeader // ModelLODHeader_t
	{
		public MeshHeader[] MeshHeaders;
		public float		SwitchPoint;

		private static ModelLodHeader LoadItem(BinaryReader reader)
		{
			var startSeek = reader.BaseStream.Position;
			return new ModelLodHeader
			{
				MeshHeaders = MeshHeader.Load(reader, count: reader.ReadInt32(), offset: startSeek + reader.ReadInt32()),
				SwitchPoint = reader.ReadSingle()
			};
		}

		public static ModelLodHeader[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new ModelLodHeader[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 12;
			for (var i = 0; i < count; i++)
			{
				reader.BaseStream.Seek(offset + (i * itemSize), SeekOrigin.Begin);
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// This maps one to one with models in the mdl file.
	// There are a bunch of model LODs stored inside potentially due to the qc $lod command
	public class ModelHeader // ModelHeader_t
	{
		public ModelLodHeader[] ModelLodHeaders;

		private static ModelHeader LoadItem(BinaryReader reader)
		{
			var startSeek = reader.BaseStream.Position;
			return new ModelHeader
			{
				ModelLodHeaders = ModelLodHeader.Load(reader, count: reader.ReadInt32(), offset: startSeek + reader.ReadInt32())
			};
		}

		public static ModelHeader[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new ModelHeader[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 8;
			for (var i = 0; i < count; i++)
			{
				reader.BaseStream.Seek(offset + (i * itemSize), SeekOrigin.Begin);
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
		
	public class BodyPartHeader // BodyPartHeader_t
	{
		public ModelHeader[] ModelHeaders;

		private static BodyPartHeader LoadItem(BinaryReader reader)
		{
			var startSeek = reader.BaseStream.Position;
			return new BodyPartHeader
			{
				ModelHeaders = ModelHeader.Load(reader, count: reader.ReadInt32(), offset: startSeek + reader.ReadInt32())
			};
		}

		public static BodyPartHeader[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new BodyPartHeader[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 8;
			for (var i = 0; i < count; i++)
			{
				reader.BaseStream.Seek(offset + (i * itemSize), SeekOrigin.Begin);
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};
		
	[DebuggerDisplay("MaterialId {MaterialId}, MaterialName {MaterialName}")]
	public class MaterialReplacementHeader // MaterialReplacementHeader_t
	{
		public short  MaterialId;
		public string MaterialName;


		private static MaterialReplacementHeader LoadItem(BinaryReader reader)
		{
			// TODO: this seems to be incorrect?
			return new MaterialReplacementHeader
			{
				MaterialId	 = reader.ReadInt16(),
				MaterialName = reader.ReadNullString(reader.ReadInt32())
			};
		}

		public static MaterialReplacementHeader[] Load(BinaryReader reader, int count, long offset)
		{
			var items = new MaterialReplacementHeader[count];
			if (count == 0 || offset == 0)
				return items;

			var startSeek = reader.BaseStream.Position;
			const int itemSize = 6;
			for (var i = 0; i < count; i++)
			{
				reader.BaseStream.Seek(offset + (i * itemSize), SeekOrigin.Begin);
				items[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startSeek, SeekOrigin.Begin);
			return items;
		}
	};

	// vtxheader_t
	public class VTX // FileHeader_t
	{
		// file version as defined by OPTIMIZED_MODEL_FILE_VERSION
		public int    Version; // OPTIMIZED_MODEL_FILE_VERSION 7

		// hardware params that affect how the model is to be optimized.
		public int    VertCacheSize;
		public ushort MaxBonesPerStrip;
		public ushort MaxBonesPerTri;
		public int    MaxBonesPerVert;

		public int    CheckSum;   // must match checkSum in the .mdl
		public int    NumLods;    // garymcthack - this is also specified in ModelHeader_t and should match
			
		public MaterialReplacementHeader[]	MaterialReplacementHeaders;
		public BodyPartHeader[]				BodyPartHeaders;

			
		public static VTX Read(BinaryReader reader)
		{
			var vtx = new VTX
			{
				Version			 = reader.ReadInt32(),
				VertCacheSize	 = reader.ReadInt32(),
				MaxBonesPerStrip = reader.ReadUInt16(),
				MaxBonesPerTri	 = reader.ReadUInt16(),
				MaxBonesPerVert	 = reader.ReadInt32(),
				CheckSum		 = reader.ReadInt32(),
				NumLods			 = reader.ReadInt32()
			};
				
			vtx.MaterialReplacementHeaders = MaterialReplacementHeader.Load(reader, count: vtx.NumLods, offset: reader.ReadInt32());
			vtx.BodyPartHeaders			   = BodyPartHeader.Load(reader, count: reader.ReadInt32(), offset: reader.ReadInt32());
			return vtx;
		}
	};	
}
