using System.Collections.Generic;

using UnityEngine;

namespace Chisel.Import.Source.VPKTools
{
	/// <summary>
	/// Finds the Unity shader that stands in for a Source shader, by name.
	/// <para>
	/// A VMT's first token is the name of the Source shader it wants ("LightmappedGeneric",
	/// "VertexLitGeneric", "UnlitGeneric", "Water", "Refract", ...). Where a Unity shader called
	/// <c>Chisel/Source/&lt;that name&gt;</c> exists, the importer builds the material against it and
	/// can carry the VMT's parameters across as they were written. Where one does not, the importer
	/// falls back to approximating the material with Unity's Standard shader, which is what it has
	/// always done.
	/// </para>
	/// <para>
	/// This means support for a new Source shader is added by dropping in a shader asset with the
	/// matching name - no importer change needed.
	/// </para>
	/// </summary>
	public static class SourceShaderLookup
	{
		public const string kShaderNamePrefix = "Chisel/Source/";

		// Shader.Find is a project-wide search, and ImportVMT runs it once per material; misses are
		// cached too, since a Source shader we have no equivalent for will keep on not having one.
		readonly static Dictionary<string, Shader> s_Lookup = new();

		// Source shaders that are another one under a different name. On DX9, WorldVertexTransition IS
		// LightmappedGeneric with a $basetexture2: worldvertextransition.cpp draws with LightmappedGeneric's
		// own helper. An alias wins over a shader asset of the aliased name, since it states what the
		// Source shader is rather than approximating it.
		readonly static Dictionary<string, string> s_Aliases = new(System.StringComparer.OrdinalIgnoreCase)
		{
			{ "WorldVertexTransition", "LightmappedGeneric" },
		};

		/// <summary>The Source shader a VMT's shader name stands for, aliases resolved - which is what
		/// decides what the material's parameters mean.</summary>
		public static string Canonical(string sourceShaderName)
		{
			if (string.IsNullOrWhiteSpace(sourceShaderName))
				return sourceShaderName;
			var name = sourceShaderName.Trim();
			return s_Aliases.TryGetValue(name, out var alias) ? alias : name;
		}

		/// <summary>Returns the Unity shader standing in for <paramref name="sourceShaderName"/>, or
		/// null when there is none and the caller should fall back to its own approximation.</summary>
		public static Shader Find(string sourceShaderName)
		{
			if (string.IsNullOrWhiteSpace(sourceShaderName))
				return null;

			if (s_Lookup.TryGetValue(sourceShaderName, out var shader))
				return shader;

			// Shader.Find is CASE SENSITIVE, and VMTs spell the same shader every which way -
			// "LightmappedGeneric", "LightMappedGeneric", "lightmappedgeneric", "vertexlitGeneric".
			// Matching on the VMT's spelling therefore silently missed whole families of materials
			// and dropped them on the Standard-shader fallback, so the name is resolved against an
			// index of what the project actually has instead.
			if (!EnsureIndex().TryGetValue(Canonical(sourceShaderName), out shader))
				shader = null;
			s_Lookup[sourceShaderName] = shader;
			return shader;
		}

		// Keyed on the part after "Chisel/Source/", compared case-insensitively. Built once: this is
		// a project-wide shader enumeration, and a Source shader we have no equivalent for will keep
		// on not having one for the rest of the session.
		static Dictionary<string, Shader> s_Index;

		static Dictionary<string, Shader> EnsureIndex()
		{
			if (s_Index != null)
				return s_Index;

			s_Index = new Dictionary<string, Shader>(System.StringComparer.OrdinalIgnoreCase);
			foreach (var info in UnityEditor.ShaderUtil.GetAllShaderInfo())
			{
				if (!info.name.StartsWith(kShaderNamePrefix, System.StringComparison.OrdinalIgnoreCase))
					continue;
				var shader = Shader.Find(info.name);
				if (shader == null)
					continue;
				// A shader that does not compile is worse than the fallback: the material would be
				// magenta rather than merely approximate.
				if (info.hasErrors)
				{
					Debug.LogWarning($"Source shader \"{info.name}\" has compile errors and will not be used; " +
									  "materials asking for it fall back to the Standard shader.");
					continue;
				}
				s_Index[info.name.Substring(kShaderNamePrefix.Length)] = shader;
			}
			return s_Index;
		}
	}
}
