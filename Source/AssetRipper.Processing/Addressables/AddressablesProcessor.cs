using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
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
	private sealed class SettingsJsonData
	{
		public string? m_buildTarget { get; set; }
		public string? m_SettingsHash { get; set; }
		public string? m_AddressablesVersion { get; set; }
		public int m_maxConcurrentWebRequests { get; set; }
		public int m_CatalogRequestsTimeout { get; set; }
		public bool m_IsLocalCatalogInBundle { get; set; }
		public bool m_DisableCatalogUpdateOnStart { get; set; }
	}

	public void Process(GameData gameData)
	{
		Logger.Info(LogCategory.Processing, "Starting advanced Addressables reconstruction...");

		string? aaPath = FindAddressablesFolder(gameData.PlatformStructure);
		if (aaPath == null)
		{
			Logger.Info(LogCategory.Processing, "Addressables directory not found in StreamingAssets. Skipping reconstruction.");
			return;
		}

		string settingsPath = Path.Combine(aaPath, "settings.json");
		SettingsJsonData? settingsData = null;
		if (gameData.PlatformStructure!.FileSystem.File.Exists(settingsPath))
		{
			try
			{
				string settingsJson = gameData.PlatformStructure.FileSystem.File.ReadAllText(settingsPath);
				settingsData = JsonSerializer.Deserialize<SettingsJsonData>(settingsJson);
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Processing, $"Failed to deserialize settings.json: {ex.Message}");
			}
		}

		List<string> internalIds = new();
		string? catalogJsonPath = FindCatalog(aaPath, "catalog.json", gameData.PlatformStructure);
		string? catalogBinPath = FindCatalog(aaPath, "catalog.bin", gameData.PlatformStructure);

		if (catalogJsonPath != null)
		{
			try
			{
				string json = gameData.PlatformStructure.FileSystem.File.ReadAllText(catalogJsonPath);
				AddressablesCatalog? catalog = AddressablesCatalogParser.ParseJson(json);
				if (catalog?.m_InternalIds != null)
				{
					internalIds.AddRange(catalog.m_InternalIds);
				}
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Processing, $"Failed to parse JSON catalog: {ex.Message}");
			}
		}
		else if (catalogBinPath != null)
		{
			try
			{
				byte[] binData = gameData.PlatformStructure.FileSystem.File.ReadAllBytes(catalogBinPath);
				internalIds = ExtractStringsFromBinaryCatalog(binData);
				Logger.Info(LogCategory.Processing, $"Extracted {internalIds.Count} path strings from binary catalog.");
			}
			catch (Exception ex)
			{
				Logger.Error(LogCategory.Processing, $"Failed to process binary catalog: {ex.Message}");
			}
		}

		IMonoBehaviour? settingsAsset = null;
		List<IMonoBehaviour> groupAssets = new();

		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			if (asset is IMonoBehaviour monoBehaviour)
			{
				if (monoBehaviour.IsAddressableAssetSettings())
				{
					settingsAsset = monoBehaviour;
				}
				else if (monoBehaviour.IsAddressableAssetGroup())
				{
					groupAssets.Add(monoBehaviour);
				}
			}
		}

		if (settingsAsset != null)
		{
			ApplySettingsToAsset(settingsAsset, settingsData, groupAssets);
		}

		ReassembleGroupsAndAlignAssets(gameData, internalIds, groupAssets);
		TranslateAssetReferenceGuids(gameData);
	}

	private static void ApplySettingsToAsset(IMonoBehaviour settingsAsset, SettingsJsonData? data, List<IMonoBehaviour> groups)
	{
		SerializableStructure? structure = settingsAsset.LoadStructure();
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

	private static void ReassembleGroupsAndAlignAssets(GameData gameData, List<string> internalIds, List<IMonoBehaviour> groups)
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

		foreach (string internalId in internalIds)
		{
			if (string.IsNullOrEmpty(internalId)) continue;

			string normalizedId = internalId.Replace('\\', '/');
			if (normalizedId.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
			{
				if (assetLookup.TryGetValue(normalizedId, out IUnityObjectBase? matchingAsset))
				{
					matchingAsset.OverrideDirectory = Path.GetDirectoryName(normalizedId);
					matchingAsset.OverrideName = Path.GetFileNameWithoutExtension(normalizedId);
				}
			}
		}
	}

	private static void TranslateAssetReferenceGuids(GameData gameData)
	{
		Dictionary<string, string> translationMap = new(StringComparer.OrdinalIgnoreCase);
		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			if (asset is IEditorExtension editorExt)
			{
				string originalGuid = editorExt.AssetInfo.GUID.ToString().ToLowerInvariant();
				string currentGuid = gameData.GetExportID(asset).ToString().ToLowerInvariant();
				if (!translationMap.ContainsKey(originalGuid))
				{
					translationMap.Add(originalGuid, currentGuid);
				}
			}
		}

		foreach (IUnityObjectBase asset in gameData.GameBundle.FetchAssets())
		{
			if (asset is IMonoBehaviour monoBehaviour)
			{
				SerializableStructure? structure = monoBehaviour.LoadStructure();
				if (structure != null)
				{
					TranslateStructure(structure, translationMap);
				}
			}
		}
	}

	private static void TranslateStructure(SerializableStructure structure, Dictionary<string, string> translationMap)
	{
		for (int i = 0; i < structure.Fields.Length; i++)
		{
			ref SerializableValue field = ref structure.Fields[i];
			if (field.CValue is SerializableStructure childStructure)
			{
				if (childStructure.Type.Name == "AssetReference" || childStructure.ContainsField("m_AssetGUID"))
				{
					if (childStructure.TryGetField("m_AssetGUID", out SerializableValue guidField))
					{
						string rawGuid = guidField.AsString.ToLowerInvariant();
						if (translationMap.TryGetValue(rawGuid, out string? mappedGuid))
						{
							guidField.AsString = mappedGuid;
						}
					}
				}
				else
				{
					TranslateStructure(childStructure, translationMap);
				}
			}
			else if (field.AsAssetArray != null)
			{
				foreach (var element in field.AsAssetArray)
				{
					if (element is SerializableStructure elementStructure)
					{
						TranslateStructure(elementStructure, translationMap);
					}
				}
			}
		}
	}

	private static List<string> ExtractStringsFromBinaryCatalog(byte[] data)
	{
		List<string> result = new();
		int index = 0;
		int len = data.Length;

		while (index < len)
		{
			if (index + 7 < len &&
				data[index] == 'A' &&
				data[index + 1] == 's' &&
				data[index + 2] == 's' &&
				data[index + 3] == 'e' &&
				data[index + 4] == 't' &&
				data[index + 5] == 's' &&
				data[index + 6] == '/')
			{
				int start = index;
				while (index < len && data[index] >= 32 && data[index] <= 126)
				{
					index++;
				}
				if (index > start)
				{
					string path = Encoding.ASCII.GetString(data, start, index - start);
					result.Add(path);
				}
			}
			else
			{
				index++;
			}
		}
		return result;
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

	private static string? FindCatalog(string aaPath, string pattern, PlatformGameStructure? platform)
	{
		if (platform == null) return null;

		foreach (string file in platform.FileSystem.Directory.EnumerateFiles(aaPath, "*", SearchOption.AllDirectories))
		{
			string fileName = Path.GetFileName(file);
			if (fileName.Contains("catalog", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
			{
				return file;
			}
		}

		return null;
	}
}