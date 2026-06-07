using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.UnityProjects.Addressables;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Import.Structure.Platforms;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AssetRipper.Processing.Addressables;

public class AddressablesProcessor : IAssetProcessor
{
	public void Process(GameData gameData)
	{
		Logger.Info(LogCategory.Processing, "Starting advanced Addressables reconstruction...");

		if (gameData.PlatformStructure == null)
		{
			Logger.Info(LogCategory.Processing, "Platform structure is null. Skipping Addressables reconstruction.");
			return;
		}

		string? aaPath = FindAddressablesFolder(gameData.PlatformStructure);
		if (aaPath == null)
		{
			Logger.Info(LogCategory.Processing, "Addressables directory not found in StreamingAssets. Skipping reconstruction.");
			return;
		}

		string settingsPath = Path.Combine(aaPath, "settings.json");
		AddressablesSettingsData? settingsData = null;
		if (gameData.PlatformStructure.FileSystem.File.Exists(settingsPath))
		{
			try
			{
				string settingsJson = gameData.PlatformStructure.FileSystem.File.ReadAllText(settingsPath);
				settingsData = JsonSerializer.Deserialize(settingsJson, AddressablesJsonContext.Default.AddressablesSettingsData);
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Processing, $"Failed to deserialize settings.json: {ex.Message}");
			}
		}

		List<CompactCatalogEntry> entries = new();
		string? catalogJsonPath = FindCatalog(aaPath, ".json", gameData.PlatformStructure);
		string? catalogBinPath = FindCatalog(aaPath, ".bin", gameData.PlatformStructure);

		if (catalogJsonPath != null)
		{
			try
			{
				string json = gameData.PlatformStructure.FileSystem.File.ReadAllText(catalogJsonPath);
				AddressablesCatalog? catalog = AddressablesCatalogParser.ParseJson(json);
				if (catalog != null && catalog.InternalIds != null && catalog.ResourceTypes != null)
				{
					entries = JsonCatalogDecoder.Decode(
						catalog.ResourceTypes, // provider/type descriptions mapped as ResourceTypes
						catalog.InternalIds,
						catalog.KeyDataString ?? "",
						catalog.BucketDataString ?? "",
						catalog.EntryDataString ?? "",
						catalog.ExtraDataString ?? ""
					);
				}
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Processing, $"Failed to process JSON catalog: {ex.Message}");
			}
		}
		else if (catalogBinPath != null)
		{
			try
			{
				byte[] binData = gameData.PlatformStructure.FileSystem.File.ReadAllBytes(catalogBinPath);
				BinaryCatalogReader reader = new(binData);
				entries = reader.Parse();
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Processing, $"Failed to process binary catalog: {ex.Message}");
			}
		}

		Logger.Info(LogCategory.Processing, $"Successfully unpacked {entries.Count} addressable resource entries.");

		IMonoBehaviour? settingsAsset = null;
		List<IMonoBehaviour> groupAssets = new();

		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			if (asset is IMonoBehaviour monoBehaviour)
			{
				if (IsAddressableAssetSettings(monoBehaviour))
				{
					settingsAsset = monoBehaviour;
				}
				else if (IsAddressableAssetGroup(monoBehaviour))
				{
					groupAssets.Add(monoBehaviour);
				}
			}
		}

		if (settingsAsset != null)
		{
			ApplySettingsToAsset(settingsAsset, settingsData, groupAssets);
		}

		ReassembleGroupsAndAlignAssets(gameData, entries, groupAssets);
	}

	private static bool IsAddressableAssetSettings(IMonoBehaviour monoBehaviour)
	{
		var script = monoBehaviour.ScriptP;
		return script != null && script.Namespace.String == "UnityEngine.AddressableAssets" && script.ClassName_R.String == "AddressableAssetSettings";
	}

	private static bool IsAddressableAssetGroup(IMonoBehaviour monoBehaviour)
	{
		var script = monoBehaviour.ScriptP;
		return script != null && script.Namespace.String == "UnityEngine.AddressableAssets" && script.ClassName_R.String == "AddressableAssetGroup";
	}

	private static void ApplySettingsToAsset(IMonoBehaviour settingsAsset, AddressablesSettingsData? data, List<IMonoBehaviour> groups)
	{
		SerializableStructure? structure = null;
		if (settingsAsset.Structure is UnloadedStructure unloaded)
		{
			structure = unloaded.LoadStructure();
		}
		else if (settingsAsset.Structure is SerializableStructure loaded)
		{
			structure = loaded;
		}

		if (structure == null) return;

		if (data != null)
		{
			if (structure.TryGetField("m_MaxConcurrentRequests", out SerializableValue maxRequests))
			{
				maxRequests.AsInt32 = data.m_maxConcurrentWebRequests;
			}
			if (structure.TryGetField("m_CatalogRequestsTimeout", out SerializableValue timeout))
			{
				timeout.AsInt32 = data.m_CatalogRequestsTimeout;
			}
			if (structure.TryGetField("m_DisableCatalogUpdateOnStart", out SerializableValue disableUpdate))
			{
				disableUpdate.AsBoolean = data.m_DisableCatalogUpdateOnStart;
			}
			if (structure.TryGetField("m_IsLocalCatalogInBundle", out SerializableValue inBundle))
			{
				inBundle.AsBoolean = data.m_IsLocalCatalogInBundle;
			}
		}

		settingsAsset.OverrideDirectory = "Assets/AddressableAssetsData";
		settingsAsset.OverrideName = "AddressableAssetSettings";

		foreach (IMonoBehaviour group in groups)
		{
			group.OverrideDirectory = "Assets/AddressableAssetsData/AssetGroups";
		}
	}

	private static void ReassembleGroupsAndAlignAssets(GameData gameData, List<CompactCatalogEntry> entries, List<IMonoBehaviour> groups)
	{
		Dictionary<string, IUnityObjectBase> assetLookup = new(StringComparer.OrdinalIgnoreCase);
		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			if (asset is INamed named && !string.IsNullOrEmpty(named.Name))
			{
				string key = named.Name;
				if (!assetLookup.ContainsKey(key))
				{
					assetLookup.Add(key, asset);
				}
			}
		}

		foreach (CompactCatalogEntry entry in entries)
		{
			string internalId = entry.InternalId;
			if (string.IsNullOrEmpty(internalId)) continue;

			string normalizedId = internalId.Replace('\\', '/');
			if (normalizedId.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
				normalizedId.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
			{
				string assetName = Path.GetFileNameWithoutExtension(normalizedId);
				if (assetLookup.TryGetValue(assetName, out IUnityObjectBase? matchingAsset))
				{
					matchingAsset.OverrideDirectory = Path.GetDirectoryName(normalizedId);
					matchingAsset.OverrideName = assetName;
				}
			}
		}
	}

	private static string? FindAddressablesFolder(PlatformGameStructure? platform)
	{
		if (platform == null) return null;

		string? streamingAssetsPath = platform.StreamingAssetsPath;
		if (string.IsNullOrEmpty(streamingAssetsPath) || !platform.FileSystem.Directory.Exists(streamingAssetsPath))
		{
			return null;
		}

		string targetFolder = Path.Combine(streamingAssetsPath, "aa");
		if (platform.FileSystem.Directory.Exists(targetFolder))
		{
			return targetFolder;
		}

		return streamingAssetsPath;
	}

	private static string? FindCatalog(string aaPath, string extension, PlatformGameStructure? platform)
	{
		if (platform == null) return null;

		foreach (string file in platform.FileSystem.Directory.EnumerateFiles(aaPath, "*", SearchOption.AllDirectories))
		{
			string fileName = Path.GetFileName(file);
			if (fileName.Contains("catalog", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
			{
				return file;
			}
		}

		return null;
	}
}