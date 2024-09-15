using System;
using System.IO;
using UnityEngine;
using DebuggerDisplayAttribute = System.Diagnostics.DebuggerDisplayAttribute;

namespace Chisel.Import.Source.VPKTools
{
	[DebuggerDisplay("Position {Position} Normal {Normal} TexCoord {TexCoord}")]
	public class StudioVertex // mstudiovertex_t
	{
		public Vector3	Position;
		public Vector3	Normal;
		public Vector2	TexCoord;
		public byte[]	BoneIndices;
		public float[]	BoneWeights;
			
		private static StudioVertex LoadItem(BinaryReader reader)
		{
			var boneWeights	= reader.ReadFloats(SourceEngineConstants.MaxNumBonesPerVert);
			var boneIndices = reader.ReadBytes(SourceEngineConstants.MaxNumBonesPerVert);
			var numBones	= reader.ReadByte();

			Array.Resize(ref boneWeights, numBones);
			Array.Resize(ref boneIndices, numBones);

			return new StudioVertex
			{
				Position	= reader.ReadVector3(),
				Normal		= reader.ReadVector3(),
				TexCoord	= reader.ReadVector2(),
				BoneIndices = boneIndices,
				BoneWeights = boneWeights,
			};
		}
			
		public static StudioVertex[] Load(BinaryReader reader, int count, int offset)
		{
			if (offset == 0)
				return null;

			var startPosition = reader.BaseStream.Position;

			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

			var vertices = new StudioVertex[count];
			for (var i = 0; i < count; i++)
			{
				vertices[i] = LoadItem(reader);
			}

			reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
			return vertices;
		}
	};

	public class ThinModelVertices // thinModelVertices_t
	{
		// int			m_numBoneInfluences;// Number of bone influences per vertex, N
		// float[]		m_boneWeights;		// This array stores (N-1) weights per vertex (unless N is zero)
		// char[]		m_boneIndices;		// This array stores N indices per vertex
		// Vector3[]	m_vecPositions;
		// ushort[]		m_vecNormals;		// Normals are compressed into 16 bits apiece (see PackNormal_UBYTE4() )
			
		// ??

	}
				
	public class VertexFileFixup // vertexFileFixup_t
	{
		public int lod;				// used to skip culled root lod
		public int sourceVertexID;	// absolute index from start of vertex/tangent blocks
		public int numVertexes;
			
		private static VertexFileFixup LoadItem(BinaryReader reader)
		{
			return new VertexFileFixup
			{
				lod				= reader.ReadInt32(),
				sourceVertexID	= reader.ReadInt32(),
				numVertexes		= reader.ReadInt32()
			};
		}

		public static void Fixup(BinaryReader reader, int count, int offset, VvdHeader vvd, int rootLod)
		{
			for (int i = 0; i < rootLod; i++)
			{
				vvd.NumLodVertexes[i] = vvd.NumLodVertexes[rootLod];
			}

			if (offset == 0 || count == 0)
				return;

			var startPosition = reader.BaseStream.Position;

			reader.BaseStream.Seek(offset, SeekOrigin.Begin);

			var fixupTable = new VertexFileFixup[count];
			for (var i = 0; i < count; i++)
			{
				fixupTable[i] = LoadItem(reader);
			}
				

			// fixups required
			// re-establish mesh ordered vertexes into cache memory, according to table
			int target      = 0;
			var newVertices = (vvd.Vertices != null) ? new StudioVertex[vvd.Vertices.Length] : null;
			var newTangents = (vvd.Tangents != null) ? new Vector4[vvd.Tangents.Length] : null;
			for (int i = 0; i < fixupTable.Length; i++)
			{
				if (fixupTable[i].lod < rootLod)
				{
					// working bottom up, skip over copying higher detail lods
					continue;
				}

				// copy vertexes
				if (vvd.Vertices != null)
					Array.Copy(vvd.Vertices, fixupTable[i].sourceVertexID, 
								newVertices, target, 
								fixupTable[i].numVertexes);
					
				// copy tangents
				if (vvd.Tangents != null)
					Array.Copy(vvd.Tangents, fixupTable[i].sourceVertexID,
								newTangents, target,
								fixupTable[i].numVertexes);

				// data is placed consecutively
				target += fixupTable[i].numVertexes;
			}

			if (newVertices != null)
				Array.Resize(ref newVertices, target);

			if (newTangents != null)
				Array.Resize(ref newTangents, target);

			vvd.Vertices = newVertices;
			vvd.Tangents = newTangents;
				
			reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
		}
	};
		
	public class VvdHeader	// vertexFileHeader_t
	{
		public string				ID;					// MODEL_VERTEX_FILE_ID			VSDI
														// MODEL_VERTEX_FILE_THIN_ID	VCDI
		public int					Version;			// MODEL_VERTEX_FILE_VERSION
		public int					Checksum;			// same as .mdl, ensures sync

		public int[]				NumLodVertexes;		// num verts for desired root lod [ValveConstants.MAX_NUM_LODS]

		public VertexFileFixup[]	Fixups;
		public StudioVertex[]		Vertices;
		public ThinModelVertices[]	ThinVertices;
		public Vector4[]			Tangents;
			
		public static VvdHeader Load(BinaryReader reader, MdlHeader mdlHeader)
		{
			var vvd = new VvdHeader
			{	
				ID					= reader.ReadStringWithLength(4), // Model format ID, such as "IDST" (0x49 0x44 0x53 0x54)
				Version				= reader.ReadInt32(),
				Checksum			= reader.ReadInt32(),
			};

			if (vvd.ID == "IDSV")
			{ 
				int numLoDs			= reader.ReadInt32();
				vvd.NumLodVertexes	= reader.ReadInts(SourceEngineConstants.MaxNumLods);
								
				var numFixups		= reader.ReadInt32(); // num of vertexFileFixup_t
				var fixupTableStart = reader.ReadInt32(); // offset from base to fixup table
			
				var rootLod			= mdlHeader.RootLod;
				var numVertices		= vvd.NumLodVertexes[rootLod];
				vvd.Vertices		= StudioVertex.Load(reader, numVertices, offset: reader.ReadInt32());
				vvd.Tangents		= reader.ReadVector4Array(numVertices, offset: reader.ReadInt32());
					
				VertexFileFixup.Fixup(reader, count: numFixups, offset: fixupTableStart, vvd: vvd, rootLod: rootLod);
					
				var scale			= Mathf.Abs(mdlHeader.VertAnimFixedPointScale);
				if (vvd.Vertices != null)
				{
					for (int i = 0; i < vvd.Vertices.Length; i++)
					{
						vvd.Vertices[i].Position = vvd.Vertices[i].Position * scale;//SourceEngineUnits.VmfPositionToUnityPosition2(vvd.Vertices[i].Position * scale);
						vvd.Vertices[i].Normal   = vvd.Vertices[i].Normal;//SourceEngineUnits.VmfVectorToUnityVector2    (vvd.Vertices[i].Normal);
					}
				}

				if (vvd.Tangents != null)
				{
					for (int i = 0; i < vvd.Vertices.Length; i++)
						vvd.Tangents[i] = vvd.Tangents[i];// SourceEngineUnits.VmfVectorToUnityVector2(vvd.Tangents[i]);
				}

				if (numLoDs <= 1)
				{
					vvd.NumLodVertexes = new int[1] { numVertices };
				} else
					Array.Resize(ref vvd.NumLodVertexes, numLoDs);
			} else
			{
				Debug.Log(vvd.ID);
				if (vvd.ID == "IDCV")
				{
					// unsupported
					vvd.Vertices = null;
					vvd.Tangents = null;
				}
			}
			return vvd;
		}
	};

}
