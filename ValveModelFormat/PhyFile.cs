using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// A Source <c>.phy</c> collision model: the convex pieces of every solid, and the text that names the
	/// solids and describes their physics.
	/// <para>
	/// The geometry is IVP's compact surface format, walked the way vphysics walks it: a solid is a tree of
	/// ledges whose leaves are its convex pieces. Vertices are converted into Source's units and axes and left
	/// in whatever space studiomdl wrote them - model space, or a bone's space - which each piece records. The
	/// layout was checked against every .phy that Half-Life 2, its episodes, Lost Coast and Black Mesa ship:
	/// 11,071 files and 15,404 solids.
	/// </para>
	/// </summary>
	public sealed class PhyFile
	{
		/// <summary>One convex piece of a solid.</summary>
		public sealed class Convex
		{
			/// <summary>
			/// What studiomdl stored with the piece: 0 when its vertices are in model space
			/// (<c>$collisionmodel</c>), otherwise one more than the index of the bone whose space they are in
			/// (<c>$collisionjoints</c>).
			/// </summary>
			public int BoneReference;

			/// <summary>The bone whose space the vertices are in, or -1 when they are in model space.</summary>
			public int BoneIndex => BoneReference - 1;

			/// <summary>The piece's vertices, in Source units and axes.</summary>
			public Vector3[] Vertices;

			/// <summary>Three vertex indices per triangle, facing outward by the right-hand rule in Source's axes.</summary>
			public int[] Triangles;
		}

		public sealed class Solid
		{
			public int Index;
			public readonly List<Convex> Pieces = new();

			/// <summary>From the text: a ragdoll's solid is named after its bone.</summary>
			public string Name;

			/// <summary>From the text: the surface property, which decides sounds, friction and impacts.</summary>
			public string SurfaceProp;

			/// <summary>From the text: the mass, in kilograms.</summary>
			public float Mass;
		}

		/// <summary>The checksum of the .mdl this was compiled with (<c>studiohdr_t.checksum</c>).</summary>
		public int Checksum;

		public readonly List<Solid> Solids = new();

		/// <summary>The text that follows the solids, as stored: solid, ragdollconstraint, collisionrules and so on.</summary>
		public string Text = "";

		// phyheader_t, compactsurfaceheader_t and IVP's compact structures; offsets are from the start of each.
		const int   kSolidCountOffset    = 8;
		const int   kChecksumOffset      = 12;
		const int   kVphyId              = 'V' | ('P' << 8) | ('H' << 16) | ('Y' << 24);
		const int   kVphyModelTypeOffset = 6;
		const int   kCollidePoly         = 0;
		const int   kVphyHeaderSize      = 28;
		const int   kLedgeTreeRootOffset = 32;
		const int   kLedgeTreeNodeSize   = 28;
		const int   kLedgeHeaderSize     = 16;
		const int   kTriangleSize        = 16;
		const int   kPointSize           = 16;
		const float kInchesPerMeter      = 1.0f / 0.0254f;

		public static PhyFile Read(byte[] bytes)
		{
			if (bytes == null)
				throw new ArgumentNullException(nameof(bytes));

			var file = new PhyFile { Checksum = ReadInt32(bytes, kChecksumOffset) };
			var solidCount = ReadInt32(bytes, kSolidCountOffset);

			// The header's first field is its own size, and the solids start right after it.
			var position = ReadInt32(bytes, 0);
			for (int index = 0; index < solidCount; index++)
			{
				var size = ReadInt32(bytes, position);
				var body = position + 4;
				position = body + size;

				// Every solid is listed, pieces or not, so that the text's solid indices keep matching.
				var solid = new Solid { Index = index };
				file.Solids.Add(solid);

				// A VPHY solid starts with compactsurfaceheader_t; a legacy one is the IVP surface itself.
				var surface = body;
				if (ReadInt32(bytes, body) == kVphyId)
				{
					// Only a COLLIDE_POLY solid holds an IVP compact surface.
					if (ReadInt16(bytes, body + kVphyModelTypeOffset) != kCollidePoly)
						continue;
					surface = body + kVphyHeaderSize;
				}
				ReadPieces(bytes, surface + ReadInt32(bytes, surface + kLedgeTreeRootOffset), solid.Pieces);
			}

			// The text runs from the end of the last solid up to a terminating NUL.
			position = Math.Min(position, bytes.Length);
			var end = Array.IndexOf(bytes, (byte)0, position);
			if (end < 0)
				end = bytes.Length;
			file.Text = Encoding.GetEncoding(28591).GetString(bytes, position, end - position);

			ReadSolidDescriptions(file);
			return file;
		}

		// Walks the ledge tree left before right, as vphysics does. Only a terminal node's ledge is collision
		// geometry: an internal node's ledge is the convex hull of its whole subtree, kept for broadphase, and a
		// collider built from it would fill in every concavity the pieces leave open.
		static void ReadPieces(byte[] bytes, int root, List<Convex> pieces)
		{
			var nodes = new Stack<int>();
			nodes.Push(root);
			while (nodes.Count > 0)
			{
				var node = nodes.Pop();
				var rightOffset = ReadInt32(bytes, node);
				if (rightOffset != 0)
				{
					nodes.Push(node + rightOffset);
					nodes.Push(node + kLedgeTreeNodeSize);
					continue;
				}

				var ledgeOffset = ReadInt32(bytes, node + 4);
				if (ledgeOffset != 0)
					pieces.Add(ReadPiece(bytes, node + ledgeOffset));
			}
		}

		static Convex ReadPiece(byte[] bytes, int ledge)
		{
			var pointsOffset  = ReadInt32(bytes, ledge);
			var boneReference = ReadInt32(bytes, ledge + 4);
			var triangleCount = ReadInt16(bytes, ledge + 12);

			// The pieces of a solid can share one point array, so each piece gets a compact copy of just the
			// points its triangles use.
			var localIndex = new Dictionary<int, int>();
			var used       = new List<int>();
			var triangles  = new int[triangleCount * 3];
			for (int t = 0; t < triangleCount; t++)
			{
				var triangle = ledge + kLedgeHeaderSize + (t * kTriangleSize);
				for (int e = 0; e < 3; e++)
				{
					// IVP_Compact_Edge: the low 16 bits are the point the edge starts from, and the edges of a
					// triangle run in order, so their start points are its corners.
					var point = ReadInt32(bytes, triangle + 4 + (e * 4)) & 0xFFFF;
					if (!localIndex.TryGetValue(point, out var index))
					{
						index = used.Count;
						localIndex[point] = index;
						used.Add(point);
					}
					triangles[(t * 3) + e] = index;
				}
			}

			var vertices = new Vector3[used.Count];
			for (int i = 0; i < used.Count; i++)
			{
				var point = ledge + pointsOffset + (used[i] * kPointSize);
				var x = ReadSingle(bytes, point);
				var y = ReadSingle(bytes, point + 4);
				var z = ReadSingle(bytes, point + 8);
				// IVP is y-down metres, Source z-up inches: vphysics' ConvertPositionToHL is (x, z, -y) / 0.0254.
				// That is a rotation, not a mirror, so the triangles keep facing outward.
				vertices[i] = new Vector3(x * kInchesPerMeter, z * kInchesPerMeter, -y * kInchesPerMeter);
			}

			return new Convex { BoneReference = boneReference, Vertices = vertices, Triangles = triangles };
		}

		#region Text

		readonly struct Token
		{
			public readonly string Text;
			public readonly bool   IsOpen;
			public readonly bool   IsClose;

			public Token(string text, bool isOpen = false, bool isClose = false)
			{
				Text    = text;
				IsOpen  = isOpen;
				IsClose = isClose;
			}

			public bool IsString => !IsOpen && !IsClose;
		}

		// The text is KeyValues: blocks such as `solid { "index" "0" "name" "..." "mass" "30" }`, followed by
		// ragdollconstraint, collisionrules, editparams and others. Only what identifies each solid is read out of
		// it; Text keeps all of it.
		static void ReadSolidDescriptions(PhyFile file)
		{
			var tokens = Tokenize(file.Text);
			var position = 0;
			while (position < tokens.Count)
			{
				if (!IsBlockStart(tokens, position))
				{
					position++;
					continue;
				}

				var name  = tokens[position].Text;
				var pairs = ReadBlock(tokens, ref position);
				if (string.Equals(name, "solid", StringComparison.OrdinalIgnoreCase))
					DescribeSolid(file, pairs);
			}
		}

		static void DescribeSolid(PhyFile file, List<(string key, string value)> pairs)
		{
			var index = -1;
			string name = null, surfaceProp = null;
			var mass = 0.0f;
			foreach (var (key, value) in pairs)
			{
				switch (key.ToLowerInvariant())
				{
					case "index":       int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index); break;
					case "name":        name = value; break;
					case "surfaceprop": surfaceProp = value; break;
					case "mass":        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out mass); break;
				}
			}

			if (index < 0 || index >= file.Solids.Count)
				return;
			var solid = file.Solids[index];
			solid.Name        = name;
			solid.SurfaceProp = surfaceProp;
			solid.Mass        = mass;
		}

		static bool IsBlockStart(List<Token> tokens, int position)
		{
			return tokens[position].IsString && position + 1 < tokens.Count && tokens[position + 1].IsOpen;
		}

		// Reads the block whose name is at position and leaves position after its closing brace. Returns its
		// key-value pairs; a block nested inside it is skipped over.
		static List<(string key, string value)> ReadBlock(List<Token> tokens, ref int position)
		{
			var pairs = new List<(string key, string value)>();
			position += 2;
			while (position < tokens.Count && !tokens[position].IsClose)
			{
				if (IsBlockStart(tokens, position))
				{
					ReadBlock(tokens, ref position);
				}
				else if (tokens[position].IsString && position + 1 < tokens.Count && tokens[position + 1].IsString)
				{
					pairs.Add((tokens[position].Text, tokens[position + 1].Text));
					position += 2;
				}
				else
				{
					position++;
				}
			}
			position++;
			return pairs;
		}

		static List<Token> Tokenize(string text)
		{
			var tokens = new List<Token>();
			var i = 0;
			while (i < text.Length)
			{
				var c = text[i];
				if (char.IsWhiteSpace(c))
				{
					i++;
				}
				else if (c == '{' || c == '}')
				{
					tokens.Add(new Token(null, isOpen: c == '{', isClose: c == '}'));
					i++;
				}
				else if (c == '"')
				{
					var close = text.IndexOf('"', i + 1);
					if (close < 0)
						close = text.Length;
					tokens.Add(new Token(text.Substring(i + 1, close - i - 1)));
					i = close + 1;
				}
				else
				{
					var start = i;
					while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '"')
						i++;
					tokens.Add(new Token(text.Substring(start, i - start)));
				}
			}
			return tokens;
		}

		#endregion

		static int ReadInt32(byte[] bytes, int offset)
		{
			return BitConverter.ToInt32(bytes, offset);
		}

		static short ReadInt16(byte[] bytes, int offset)
		{
			return BitConverter.ToInt16(bytes, offset);
		}

		static float ReadSingle(byte[] bytes, int offset)
		{
			return BitConverter.ToSingle(bytes, offset);
		}
	}
}
