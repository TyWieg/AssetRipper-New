// Module: AssetRipper.Export.UnityProjects
// Unity Version Context: Version-agnostic
// Performance Constraint: Low-allocation parsing

using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Import.Configuration;
using AssetRipper.IO.Files;
using AssetRipper.Primitives;
using AssetRipper.Processing.Scenes;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Classes.ClassID_141;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace AssetRipper.Export.UnityProjects;

public class ProjectAssetContainer : IExportContainer
{
	private readonly record struct ScriptIdentity(string Assembly, string Namespace, string Class);

	private sealed class ScriptIdentityComparer : IEqualityComparer<ScriptIdentity>
	{
		public static ScriptIdentityComparer Instance { get; } = new();

		public bool Equals(ScriptIdentity x, ScriptIdentity y)
		{
			return string.Equals(x.Assembly, y.Assembly, StringComparison.OrdinalIgnoreCase) &&
				   string.Equals(x.Namespace, y.Namespace, StringComparison.OrdinalIgnoreCase) &&
				   string.Equals(x.Class, y.Class, StringComparison.OrdinalIgnoreCase);
		}

		public int GetHashCode(ScriptIdentity obj)
		{
			return HashCode.Combine(
				string.GetHashCode(obj.Assembly, StringComparison.OrdinalIgnoreCase),
				string.GetHashCode(obj.Namespace, StringComparison.OrdinalIgnoreCase),
				string.GetHashCode(obj.Class, StringComparison.OrdinalIgnoreCase));
		}
	}

	public ProjectAssetContainer(ProjectExporter exporter, CoreConfiguration options, IEnumerable<IUnityObjectBase> assets,
		IReadOnlyList<IExportCollection> collections)
	{
		m_exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
		CurrentCollection = null!;

		ExportVersion = options.Version;

		m_buildSettings = assets.OfType<IBuildSettings>().FirstOrDefault();

		List<SceneExportCollection> scenes = new();
		foreach (IExportCollection collection in collections)
		{
			foreach (IUnityObjectBase asset in collection.Assets)
			{
				CheckIfAlreadyAdded(this, asset, collection);
				m_assetCollections.Add(asset, collection);
			}
			if (collection is SceneExportCollection scene)
			{
				scenes.Add(scene);
			}
		}
		m_scenes = scenes.ToArray();

		// Load inline relational mapping from an existing ScriptRelinkMap.tsv if present in the target directory
		string mapPath = Path.Combine(options.ProjectRootPath, "Assets", "Editor", "AssetRipperPatches", "ScriptRelinkMap.tsv");
		LoadInlineScriptMappings(mapPath, assets);

		[Conditional("DEBUG")]
		static void CheckIfAlreadyAdded(ProjectAssetContainer container, IUnityObjectBase asset, IExportCollection currentCollection)
		{
			if (container.m_assetCollections.TryGetValue(asset, out IExportCollection? previousCollection))
			{
				throw new ArgumentException($"Asset {asset} is already added by {previousCollection}");
			}
		}
	}

	public long GetExportID(IUnityObjectBase asset)
	{
		if (m_assetCollections.TryGetValue(asset, out IExportCollection? collection))
		{
			return collection.GetExportID(this, asset);
		}

		return ExportIdHandler.GetMainExportID(asset);
	}

	public AssetType ToExportType(Type type)
	{
		return m_exporter.ToExportType(type);
	}

	public MetaPtr CreateExportPointer(IUnityObjectBase asset)
	{
		// Intercept IMonoScript references to apply inline pre-translation
		if (asset is IMonoScript monoScript)
		{
			string assembly = NormalizeAssemblyName(AssetRipper.Import.Structure.Assembly.MonoScriptExtensions.GetAssemblyNameFixed(monoScript));
			string @namespace = monoScript.Namespace.String;
			string @class = monoScript.ClassName_R.String;

			ScriptIdentity identity = new(assembly, @namespace, @class);
			if (m_inlineScriptMappings.TryGetValue(identity, out MetaPtr preTranslatedPtr))
			{
				return preTranslatedPtr;
			}
		}

		if (m_assetCollections.TryGetValue(asset, out IExportCollection? collection))
		{
			return collection.CreateExportPointer(this, asset, collection == CurrentCollection);
		}

		return MetaPtr.CreateMissingReference(asset.ClassID, AssetType.Meta);
	}

	public UnityGuid ScenePathToGUID(string path)
	{
		foreach (SceneExportCollection scene in m_scenes)
		{
			if (scene.Scene.Path == path)
			{
				return scene.GUID;
			}
		}
		return default;
	}

	public bool IsSceneDuplicate(int sceneIndex) => SceneHelpers.IsSceneDuplicate(sceneIndex, m_buildSettings);

	public bool TryGetTranslatedGuid(string originalGuid, [NotNullWhen(true)] out string? translatedGuid)
	{
		return m_guidTranslations.TryGetValue(originalGuid, out translatedGuid);
	}

	private void LoadInlineScriptMappings(string mapPath, IEnumerable<IUnityObjectBase> assets)
	{
		if (!global::System.IO.File.Exists(mapPath))
		{
			return;
		}

		// Pre-cache script assets in the current hierarchy for efficient O(1) matching
		Dictionary<ScriptIdentity, IMonoScript> scriptLookup = new(ScriptIdentityComparer.Instance);
		foreach (IMonoScript script in assets.OfType<IMonoScript>())
		{
			string assembly = NormalizeAssemblyName(AssetRipper.Import.Structure.Assembly.MonoScriptExtensions.GetAssemblyNameFixed(script));
			string @namespace = script.Namespace.String;
			string @class = script.ClassName_R.String;
			ScriptIdentity identity = new(assembly, @namespace, @class);
			if (!scriptLookup.ContainsKey(identity))
			{
				scriptLookup.Add(identity, script);
			}
		}

		try
		{
			foreach (string rawLine in global::System.IO.File.ReadLines(mapPath))
			{
				string line = rawLine.Trim();
				if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
				{
					continue;
				}

				string[] parts = line.Split('\t');
				if (parts.Length < 6)
				{
					continue;
				}

				string originalGuidStr = parts[0].Trim().ToLowerInvariant();
				string fileIdStr = parts[1].Trim();
				string assembly = parts[2].Trim();
				string @namespace = parts[3].Trim();
				string @class = parts[4].Trim();

				ScriptIdentity identity = new(assembly, @namespace, @class);
				if (scriptLookup.TryGetValue(identity, out IMonoScript? targetScript))
				{
					MetaPtr targetPtr = CreateExportPointer(targetScript);
					m_inlineScriptMappings[identity] = targetPtr;
					m_guidTranslations[originalGuidStr] = targetPtr.GUID.ToString();
				}
				else
				{
					long fileId = 0;
					if (System.Guid.TryParse(originalGuidStr, out System.Guid sysGuid) && long.TryParse(fileIdStr, out fileId))
					{
						UnityGuid convertedGuid = new UnityGuid(sysGuid);
						m_inlineScriptMappings[identity] = new MetaPtr(fileId, convertedGuid, AssetType.Meta);
						m_guidTranslations[originalGuidStr] = originalGuidStr;
					}
				}
			}
		}
		catch
		{
			// Fallback gracefully on parsing exceptions
		}
	}

	private static string NormalizeAssemblyName(string assemblyName)
	{
		return assemblyName switch
		{
			"unity.addressables" => "Unity.Addressables",
			_ => assemblyName,
		};
	}

	public IExportCollection CurrentCollection { get; set; }
	public AssetCollection File => CurrentCollection.File;
	public UnityVersion ExportVersion { get; }

	private readonly ProjectExporter m_exporter;
	private readonly Dictionary<IUnityObjectBase, IExportCollection> m_assetCollections = new();
	private readonly Dictionary<ScriptIdentity, MetaPtr> m_inlineScriptMappings = new(ScriptIdentityComparer.Instance);
	private readonly Dictionary<string, string> m_guidTranslations = new(StringComparer.OrdinalIgnoreCase);

	private readonly IBuildSettings? m_buildSettings;
	private readonly SceneExportCollection[] m_scenes;
}