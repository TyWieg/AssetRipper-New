using AssetRipper.AssemblyDumper.Utils;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.Tpk.Shared;
using AssetRipper.Tpk.TypeTrees;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace AssetRipper.AssemblyDumper;

internal sealed class UniversalNode : IEquatable<UniversalNode>, IDeepCloneable<UniversalNode>
{
	public string TypeName { get => typeName; set => typeName = value ?? ""; }
	public string OriginalTypeName
	{
		get => string.IsNullOrEmpty(originalTypeName) ? TypeName : originalTypeName;
		set => originalTypeName = value ?? "";
	}
	public string Name { get => name; set => name = value ?? ""; }
	public string OriginalName
	{
		get => string.IsNullOrEmpty(originalName) ? Name : originalName;
		set => originalName = value ?? "";
	}
	public short Version { get; set; }
	public TransferMetaFlags MetaFlag { get; set; }
	public List<UniversalNode> SubNodes { get => subNodes; set => subNodes = value ?? new(); }

	private string originalTypeName = "";
	private string originalName = "";
	private string typeName = "";
	private string name = "";
	private List<UniversalNode> subNodes = new();

	public bool IgnoreInMetaFiles => MetaFlag.IsIgnoreInMetaFiles();
	public bool AlignBytes => MetaFlag.IsAlignBytes();
	public bool TreatIntegerAsBoolean => MetaFlag.IsTreatIntegerValueAsBoolean();
	private bool TreatIntegerAsChar => MetaFlag.IsCharPropertyMask();

	public NodeType NodeType
	{
		get
		{
			return subNodes.Count == 0
				? TypeName switch
				{
					"bool" => NodeType.Boolean,
					"char" => NodeType.UInt8,
					"SInt8" => NodeType.Int8,
					"UInt8" => NodeType.UInt8,
					"short" or "SInt16" => NodeType.Int16,
					"ushort" or "UInt16" or "unsigned short" => TreatIntegerAsChar ? NodeType.Character : NodeType.UInt16,
					"int" or "SInt32" or "Type*" or "EntityId" => NodeType.Int32,
					"uint" or "UInt32" or "unsigned int" => NodeType.UInt32,
					"SInt64" or "long long" => NodeType.Int64,
					"UInt64" or "FileSize" or "unsigned long long" => NodeType.UInt64,
					"float" => NodeType.Single,
					"double" => NodeType.Double,
					_ => NodeType.Type,
				}
				: TypeName switch
				{
					"Array" => NodeType.Array,
					"vector" or "staticvector" or "set" => NodeType.Vector,
					"map" => NodeType.Map,
					"pair" => NodeType.Pair,
					"TypelessData" => NodeType.TypelessData,
					"managedReference" or "managedRefArrayItem" => NodeType.ManagedReference,
					"string" or Passes.Pass002_RenameSubnodes.Utf8StringName => NodeType.String,
					_ => NodeType.Type,
				};
		}
	}

	public UniversalNode()
	{
	}

	public bool TryGetSubNodeByName(string nodeName, [NotNullWhen(true)] out UniversalNode? subnode)
	{
		subnode = SubNodes.SingleOrDefault(n => n.Name == nodeName);
		return subnode is not null;
	}

	public UniversalNode? TryGetSubNodeByName(string nodeName)
	{
		return SubNodes.SingleOrDefault(n => n.Name == nodeName);
	}

	public bool TryGetSubNodeByTypeAndName(string nodeTypeName, string nodeName, [NotNullWhen(true)] out UniversalNode? subnode)
	{
		subnode = SubNodes.SingleOrDefault(n => n.Name == nodeName && n.TypeName == nodeTypeName);
		return subnode is not null;
	}

	public UniversalNode GetSubNodeByName(string nodeName)
	{
		return SubNodes.Single(n => n.Name == nodeName);
	}

	public static UniversalNode FromTpkUnityNode(TpkUnityNode tpkNode, TpkStringBuffer stringBuffer, TpkUnityNodeBuffer nodeBuffer)
	{
		UniversalNode result = new();
		result.TypeName = GetFixedTypeName(stringBuffer[tpkNode.TypeName]);
		result.OriginalTypeName = result.TypeName;
		result.Name = stringBuffer[tpkNode.Name];
		result.OriginalName = result.Name;
		result.Version = tpkNode.Version;
		result.MetaFlag = (TransferMetaFlags)tpkNode.MetaFlag;
		result.SubNodes = tpkNode.SubNodes
			.Select(nodeIndex => FromTpkUnityNode(nodeBuffer[nodeIndex], stringBuffer, nodeBuffer))
			.ToList();
		return result;
	}

	private static string GetFixedTypeName(string originalName)
	{
		return originalName switch
		{
			"short" => "SInt16",
			"int" => "SInt32",
			"long long" => "SInt64",
			"unsigned short" => "UInt16",
			"unsigned int" => "UInt32",
			"unsigned long long" => "UInt64",
			_ => originalName,
		};
	}

	public UniversalNode DeepClone()
	{
		UniversalNode clone = new();
		clone.TypeName = TypeName;
		clone.originalTypeName = originalTypeName;
		clone.Name = Name;
		clone.originalName = originalName;
		clone.Version = Version;
		clone.MetaFlag = MetaFlag;
		clone.SubNodes = SubNodes.ConvertAll(x => x.DeepClone());
		return clone;
	}

	public UniversalNode ShallowClone()
	{
		UniversalNode clone = new();
		clone.TypeName = TypeName;
		clone.originalTypeName = originalTypeName;
		clone.Name = Name;
		clone.originalName = originalName;
		clone.Version = Version;
		clone.MetaFlag = MetaFlag;
		clone.SubNodes = SubNodes.ToList();
		return clone;
	}

	public UniversalNode DeepCloneAsRootNode()
	{
		UniversalNode clone = DeepClone();
		clone.Name = "Base";
		clone.OriginalName = clone.Name;
		return clone;
	}

	public UniversalNode ShallowCloneAsRootNode()
	{
		UniversalNode clone = ShallowClone();
		clone.Name = "Base";
		clone.OriginalName = clone.Name;
		return clone;
	}

	public override bool Equals(object? obj)
	{
		return Equals(obj as UniversalNode);
	}

	public bool Equals(UniversalNode? other)
	{
		return other is not null &&
			   TypeName == other.TypeName &&
			   OriginalTypeName == other.OriginalTypeName &&
			   Name == other.Name &&
			   OriginalName == other.OriginalName &&
			   Version == other.Version &&
			   MetaFlag == other.MetaFlag &&
			   EqualityComparer<List<UniversalNode>>.Default.Equals(SubNodes, other.SubNodes);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(TypeName, OriginalTypeName, Name, OriginalName, Version, MetaFlag, SubNodes);
	}

	public static bool operator ==(UniversalNode? left, UniversalNode? right)
	{
		return EqualityComparer<UniversalNode>.Default.Equals(left, right);
	}

	public static bool operator !=(UniversalNode? left, UniversalNode? right)
	{
		return !(left == right);
	}

	public override string ToString()
	{
		return $"{TypeName} {Name}";
	}
}
