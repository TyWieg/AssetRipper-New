using AssetRipper.Assets;
using AssetRipper.Import.AssetCreation;
using AssetRipper.SerializationLogic;
using System;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

public static class SerializableTypeExtensions
{
	public static SerializableStructure CreateSerializableStructure(this SerializableType type)
	{
		return new SerializableStructure(type, 0);
	}

	public static SerializableStructure CreateSerializableStructure(this SerializableType type, int depth)
	{
		return new SerializableStructure(type, depth);
	}

	public static IUnityAssetBase CreateInstance(this SerializableType type, int depth, UnityVersion version)
	{
		return CreateInstance(type, depth, version, null);
	}

	internal static IUnityAssetBase CreateInstance(this SerializableType type, int depth, UnityVersion version, ManagedReferenceResolver? managedReferenceResolver)
	{
		if (type.Name == "ManagedReferencesRegistry")
		{
			return new ManagedReferencesRegistryAsset(managedReferenceResolver);
		}
		if (type.IsEngineStruct())
		{
			return GameAssetFactory.CreateEngineAsset(type.Name, version);
		}
		return new SerializableStructure(type, depth);
	}
}