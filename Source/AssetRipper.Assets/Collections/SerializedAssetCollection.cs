using AssetRipper.Assets.Metadata;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetRipper.Assets.Collections;

public sealed class SerializedAssetCollection : AssetCollection
{
	private FileIdentifier[]? DependencyIdentifiers { get; set; }
	
	/// <summary>
	/// The serialized type references stored in the file header.
	/// Required for resolving ManagedReference ([SerializeReference]) types.
	/// </summary>
	public SerializedTypeReference[] RefTypes { get; private set; } = [];

	private SerializedAssetCollection(Bundle bundle) : base(bundle)
	{
	}

	internal static SerializedAssetCollection FromSerializedFile(Bundle bundle, SerializedFile file)
	{
		SerializedAssetCollection collection = new SerializedAssetCollection(bundle)
		{
			Name = file.Name,
			FilePath = file.FilePath,
			Version = file.Version,
			Platform = file.Platform,
			Flags = file.Flags,
			EndianType = file.EndianType,
			RefTypes = file.RefTypes.ToArray(),
		};
		ReadOnlySpan<FileIdentifier> fileDependencies = file.Dependencies;
		if (fileDependencies.Length > 0)
		{
			collection.DependencyIdentifiers = fileDependencies.ToArray();
		}
		return collection;
	}

	public override void ResolveDependencies(IReadOnlyList<AssetCollection> collections)
	{
		if (DependencyIdentifiers is null || DependencyIdentifiers.Length == 0)
		{
			return;
		}

		Dependencies.Clear();
		Dependencies.EnsureCapacity(DependencyIdentifiers.Length);
		for (int i = 0; i < DependencyIdentifiers.Length; i++)
		{
			FileIdentifier identifier = DependencyIdentifiers[i];
			AssetCollection? collection = collections.FirstOrDefault(c => c.Name == identifier.AssetPath);
			Dependencies.Add(collection);
		}
		DependencyIdentifiers = null;
	}
}
