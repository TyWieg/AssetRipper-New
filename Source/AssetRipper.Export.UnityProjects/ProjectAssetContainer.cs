using AssetRipper.Assets.Collections;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Import.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Processing.Scenes;
using AssetRipper.SourceGenerated.Classes.ClassID_141;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace AssetRipper.Export.UnityProjects;

public sealed class ProjectAssetContainer
{
	private readonly Dictionary<IUnityObjectBase, IExportCollection> m_assetCollections = new();
	private readonly SceneExportCollection[] m_scenes;

	public ProjectAssetContainer(ProjectExporter exporter, CoreConfiguration options, IEnumerable<IExportCollection> collections)
	{
		List<SceneExportCollection> scenes = new();
		foreach (IExportCollection collection in collections)
		{
			foreach (IUnityObjectBase asset in collection.Assets)
			{
				if (!m_assetCollections.TryAdd(asset, collection))
				{
					Logger.Warning(LogCategory.Export, $"Asset {asset} is already added by {m_assetCollections[asset]}. Skipping addition from {collection}.");
				}
			}
			if (collection is SceneExportCollection scene)
			{
				scenes.Add(scene);
			}
		}
		m_scenes = scenes.ToArray();
	}

	public long GetExportID(IUnityObjectBase asset)
	{
		if (m_assetCollections.TryGetValue(asset, out IExportCollection? collection))
		{
			return collection.GetExportID(asset);
		}
		throw new ArgumentException($"Asset {asset} is not inside any collection", nameof(asset));
	}

	public IExportCollection GetCollection(IUnityObjectBase asset)
	{
		if (m_assetCollections.TryGetValue(asset, out IExportCollection? collection))
		{
			return collection;
		}
		throw new ArgumentException($"Asset {asset} is not inside any collection", nameof(asset));
	}

	public bool TryGetCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? collection)
	{
		return m_assetCollections.TryGetValue(asset, out collection);
	}

	public IReadOnlyCollection<IExportCollection> Collections => m_assetCollections.Values.Distinct().ToArray();
	public IReadOnlyCollection<SceneExportCollection> Scenes => m_scenes;
}
