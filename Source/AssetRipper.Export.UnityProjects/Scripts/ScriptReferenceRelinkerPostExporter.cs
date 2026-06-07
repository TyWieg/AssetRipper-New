using AsmResolver.DotNet;
using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Scripts;

public sealed class ScriptReferenceRelinkerPostExporter : IPostExporter
{
	private const string ToolRootRelativePath = "Assets/Editor/AssetRipperPatches";
	private const string MapRelativePath = ToolRootRelativePath + "/ScriptRelinkMap.tsv";
	private const string EditorScriptRelativePath = ToolRootRelativePath + "/ScriptReferenceRelinker.cs";
	private const string Header = "# guid\tfileID\tassembly\tnamespace\tclass\tfullType\tbaseType\tname";

	public void DoPostExport(GameData gameData, FullConfiguration settings, FileSystem fileSystem)
	{
		ScriptExporter exporter = new(gameData.AssemblyManager, settings);
		List<ScriptReferenceMapEntry> entries = gameData.GameBundle.FetchAssets()
			.OfType<IMonoScript>()
			.Select(script => CreateEntry(script, exporter))
			.Distinct()
			.OrderBy(entry => entry.AssemblyName, StringComparer.Ordinal)
			.ThenBy(entry => entry.Namespace, StringComparer.Ordinal)
			.ThenBy(entry => entry.ClassName, StringComparer.Ordinal)
			.ToList();

		string toolRoot = fileSystem.Path.Join(settings.ProjectRootPath, "Assets", "Editor", "AssetRipperPatches");
		fileSystem.Directory.Create(toolRoot);

		fileSystem.File.WriteAllText(fileSystem.Path.Join(settings.ProjectRootPath, MapRelativePath), BuildMap(entries), Encoding.UTF8);
		fileSystem.File.WriteAllText(fileSystem.Path.Join(settings.ProjectRootPath, EditorScriptRelativePath), EditorScriptContents, Encoding.UTF8);
	}

	private static ScriptReferenceMapEntry CreateEntry(IMonoScript script, ScriptExporter exporter)
	{
		MetaPtr pointer = exporter.CreateExportPointer(script);
		string assemblyName = NormalizeAssemblyName(script.GetAssemblyNameFixed());
		string namespaceName = script.Namespace.String;
		string className = script.ClassName_R.String;
		string fullTypeName = script.GetFullName();
		string scriptName = script.GetNonGenericClassName();

		string baseTypeName = string.Empty;
		try
		{
			if (exporter.AssemblyManager.IsSet && script.IsScriptPresents(exporter.AssemblyManager))
			{
				TypeDefinition typeDef = script.GetTypeDefinition(exporter.AssemblyManager);
				if (typeDef?.BaseType != null)
				{
					baseTypeName = typeDef.BaseType.FullName;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Failed to resolve base type for script {fullTypeName}: {ex.Message}");
		}

		return new ScriptReferenceMapEntry(
			pointer.GUID.ToString().ToLowerInvariant(),
			pointer.FileID.ToString(),
			assemblyName,
			namespaceName,
			className,
			fullTypeName,
			baseTypeName,
			scriptName);
	}

	private static string BuildMap(IEnumerable<ScriptReferenceMapEntry> entries)
	{
		StringBuilder builder = new();
		builder.AppendLine(Header);
		foreach (ScriptReferenceMapEntry entry in entries)
		{
			builder
				.Append(Escape(entry.Guid)).Append('\t')
				.Append(Escape(entry.FileID)).Append('\t')
				.Append(Escape(entry.AssemblyName)).Append('\t')
				.Append(Escape(entry.Namespace)).Append('\t')
				.Append(Escape(entry.ClassName)).Append('\t')
				.Append(Escape(entry.FullTypeName)).Append('\t')
				.Append(Escape(entry.BaseTypeName)).Append('\t')
				.Append(Escape(entry.ScriptName))
				.AppendLine();
		}
		return builder.ToString();
	}

	private static string Escape(string value)
	{
		return value
			.Replace('\t', ' ')
			.Replace('\r', ' ')
			.Replace('\n', ' ')
			.Trim();
	}

	private static string NormalizeAssemblyName(string assemblyName)
	{
		return assemblyName switch
		{
			"unity.addressables" => "Unity.Addressables",
			_ => assemblyName,
		};
	}

	private readonly record struct ScriptReferenceMapEntry(
		string Guid,
		string FileID,
		string AssemblyName,
		string Namespace,
		string ClassName,
		string FullTypeName,
		string BaseTypeName,
		string ScriptName);

	private static string EditorScriptContents =>
		"""
		#if UNITY_EDITOR
		using System;
		using System.Collections.Concurrent;
		using System.Collections.Generic;
		using System.Globalization;
		using System.IO;
		using System.Linq;
		using System.Text;
		using System.Text.RegularExpressions;
		using System.Threading.Tasks;
		using UnityEditor;
		using UnityEditor.AddressableAssets;
		using UnityEditor.AddressableAssets.Settings;
		using UnityEngine;

		namespace AssetRipperPatches
		{
			internal static class ScriptReferenceRelinker
			{
				private const string MapRelativePath = "Assets/Editor/AssetRipperPatches/ScriptRelinkMap.tsv";
				private const long MonoScriptFileId = 11500000;
				private static readonly Regex PPtrReferenceRegex = new Regex(@"\{fileID:\s*(?<fileID>-?\d+)\s*,\s*guid:\s*(?<guid>[0-9a-fA-F]{32})\s*(?:,\s*type:\s*(?<type>\d+)\s*)?\}", RegexOptions.Compiled);
				private static bool EnableAutoRelink = true;
				private static bool VerboseLogging = true;

				static ScriptReferenceRelinker()
				{
					if (EnableAutoRelink)
					{
						EditorApplication.delayCall += TryAutoRelink;
					}
				}

				[MenuItem("Tools/AssetRipper/Relink All References")]
				private static void RelinkFromMenu()
				{
					Relink(true);
					RebuildAddressableGroups(true);
				}

				[MenuItem("Tools/AssetRipper/Recover Missing Meta Files")]
				private static void RecoverMetaFilesFromMenu()
				{
					RecoverMissingMetaFiles(true);
				}

				[MenuItem("Tools/AssetRipper/Diagnose Unresolved Scripts")]
				private static void DiagnoseFromMenu()
				{
					DiagnoseUnresolvedScripts();
				}

				[MenuItem("Tools/AssetRipper/Reconstruct Addressables Groups")]
				private static void ReconstructAddressablesFromMenu()
				{
					RebuildAddressableGroups(true);
				}

				private static void TryAutoRelink()
				{
					if (EditorApplication.isCompiling || EditorApplication.isUpdating)
					{
						EditorApplication.delayCall += TryAutoRelink;
						return;
					}
					RecoverMissingMetaFiles(false);
					Relink(false);
					RebuildAddressableGroups(false);
				}

				private static void Relink(bool verbose)
				{
					string mapPath = GetAbsoluteMapPath();
					if (!File.Exists(mapPath))
					{
						if (verbose) Debug.LogWarning("AssetRipper relink map not found at: " + mapPath);
						return;
					}
					Dictionary<string, string> sourceMap = LoadSourceMap(mapPath);
					if (sourceMap.Count == 0) return;
					if (verbose || VerboseLogging) Debug.Log("AssetRipper: Starting reference relinking...");
					Dictionary<string, (string Guid, long FileID)> installedScripts = BuildInstalledScriptMap();
					
					int changedFiles = 0;
					int changedReferences = 0;
					int skippedAlreadyCorrect = 0;
					int unresolvedCount = 0;
					ConcurrentBag<string> unresolvedIdentities = new ConcurrentBag<string>();

					try
					{
						AssetDatabase.StartAssetEditing();
						string[] candidatePaths = EnumerateCandidateAssetPaths().ToArray();
						float total = candidatePaths.Length;

						ConcurrentBag<(string Path, string Text, int Replacements, int Skipped, int Unresolved)> modifiedFiles = new ConcurrentBag<(string, string, int, int, int)>();
						
						Parallel.ForEach(candidatePaths, assetPath =>
						{
							if (TryRelinkFileFast(assetPath, sourceMap, installedScripts, out string updatedText, out int replacements, out int skipped, out int unresolved, unresolvedIdentities))
							{
								modifiedFiles.Add((assetPath, updatedText, replacements, skipped, unresolved));
							}
							else
							{
								System.Threading.Interlocked.Add(ref skippedAlreadyCorrect, skipped);
								System.Threading.Interlocked.Add(ref unresolvedCount, unresolved);
							}
						});

						int fileIndex = 0;
						foreach (var file in modifiedFiles)
						{
							if (verbose) EditorUtility.DisplayProgressBar("Relinking Assets", file.Path, (float)fileIndex++ / modifiedFiles.Count);
							try
							{
								File.WriteAllText(Path.GetFullPath(file.Path), file.Text);
								changedFiles++;
								changedReferences += file.Replacements;
								skippedAlreadyCorrect += file.Skipped;
								unresolvedCount += file.Unresolved;
							}
							catch (Exception ex)
							{
								Debug.LogError($"AssetRipper: Failed to write relinked file {file.Path}: {ex.Message}");
							}
						}
					}
					finally
					{
						AssetDatabase.StopAssetEditing();
						EditorUtility.ClearProgressBar();
					}

					if (changedFiles > 0)
					{
						AssetDatabase.Refresh();
						Debug.Log("AssetRipper successfully relinked "
							+ changedReferences.ToString(CultureInfo.InvariantCulture)
							+ " references across "
							+ changedFiles.ToString(CultureInfo.InvariantCulture)
							+ " assets. ("
							+ skippedAlreadyCorrect.ToString(CultureInfo.InvariantCulture)
							+ " already correct, left unchanged)");
					}
					else if (verbose || VerboseLogging)
					{
						Debug.Log("AssetRipper relink: No references needed updating. ("
							+ skippedAlreadyCorrect.ToString(CultureInfo.InvariantCulture)
							+ " already correct)");
					}

					if (unresolvedCount > 0 && (verbose || VerboseLogging))
					{
						StringBuilder sb = new StringBuilder();
						sb.AppendLine("AssetRipper: " + unresolvedCount.ToString(CultureInfo.InvariantCulture) + " script reference(s) could not be resolved. The following scripts were not found in the project:");
						List<string> sortedUnresolved = unresolvedIdentities.Distinct().ToList();
						sortedUnresolved.Sort(StringComparer.Ordinal);
						foreach (string identity in sortedUnresolved)
						{
							sb.AppendLine("  - " + identity.Replace("|", " / "));
						}
						Debug.LogWarning(sb.ToString());
					}
				}

				private static void RecoverMissingMetaFiles(bool verbose)
				{
					string mapPath = GetAbsoluteMapPath();
					if (!File.Exists(mapPath))
					{
						if (verbose) Debug.LogWarning("AssetRipper relink map not found at: " + mapPath);
						return;
					}
					Dictionary<string, string> identityToSourceGuid = LoadIdentityToGuidMap(mapPath);
					if (identityToSourceGuid.Count == 0) return;
					int recovered = 0;
					string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript");
					foreach (string guid in scriptGuids)
					{
						string assetPath = AssetDatabase.GUIDToAssetPath(guid);
						if (string.IsNullOrEmpty(assetPath)) continue;
						string fullPath = Path.GetFullPath(assetPath);
						string metaPath = fullPath + ".meta";
						if (File.Exists(metaPath)) continue;
						MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
						if (monoScript == null) continue;
						Type type = monoScript.GetClass();
						string identityKey;
						if (type != null)
						{
							string assemblyName = type.Assembly.GetName().Name ?? string.Empty;
							string fullTypeName = type.FullName ?? string.Empty;
							identityKey = MakeIdentityKey(assemblyName, fullTypeName);
						}
						else
						{
							string scriptName = monoScript.name;
							if (string.IsNullOrEmpty(scriptName)) continue;
							string dir = Path.GetDirectoryName(assetPath) ?? string.Empty;
							dir = dir.Replace('\\', '/');
							string inferredAssembly = TryInferAssemblyName(dir);
							string inferredNamespace = InferNamespace(dir);
							string fullTypeName = string.IsNullOrEmpty(inferredNamespace) ? scriptName : inferredNamespace + "." + scriptName;
							identityKey = MakeIdentityKey(inferredAssembly, fullTypeName);
						}
						if (!identityToSourceGuid.TryGetValue(identityKey, out string expectedGuid)) continue;
						try
						{
							string metaContent =
								"fileFormatVersion: 2\n" +
								"guid: " + expectedGuid + "\n" +
								"MonoImporter:\n" +
								"  externalObjects: {}\n" +
								"  serializedVersion: 2\n" +
								"  defaultReferences: []\n" +
								"  executionOrder: 0\n" +
								"  icon: {instanceID: 0}\n" +
								"  userData: \n" +
								"  assetBundleName: \n" +
								"  assetBundleVariant: \n";
							File.WriteAllText(metaPath, metaContent);
							recovered++;
							if (verbose || VerboseLogging)
							{
								Debug.Log("AssetRipper: Recovered .meta file for " + assetPath + " with GUID " + expectedGuid);
							}
						}
						catch (Exception ex)
						{
							Debug.LogWarning("AssetRipper: Failed to recover .meta for " + assetPath + ": " + ex.Message);
						}
					}
					if (recovered > 0)
					{
						AssetDatabase.Refresh();
						Debug.Log("AssetRipper: Recovered " + recovered.ToString(CultureInfo.InvariantCulture) + " missing .meta file(s).");
					}
					else if (verbose)
					{
						Debug.Log("AssetRipper: No missing .meta files detected.");
					}
				}

				private static void RebuildAddressableGroups(bool verbose)
				{
					string catalogPath = "Assets/StreamingAssets/aa/catalog.json";
					string catalogBinPath = "Assets/StreamingAssets/aa/catalog.bin";
					List<string> internalIds = new List<string>();

					if (File.Exists(catalogPath))
					{
						try
						{
							string json = File.ReadAllText(catalogPath);
							if (!string.IsNullOrEmpty(json))
							{
								int startIdIndex = json.IndexOf("\"m_InternalIds\"");
								if (startIdIndex >= 0)
								{
									int openBracket = json.IndexOf('[', startIdIndex);
									int closeBracket = json.IndexOf(']', openBracket);
									if (openBracket >= 0 && closeBracket >= 0)
									{
										string rawArray = json.Substring(openBracket + 1, closeBracket - openBracket - 1);
										string[] items = rawArray.Split(',');
										foreach (var item in items)
										{
											internalIds.Add(item.Trim('\"', '\\', ' ', '\r', '\n').Replace("\\\\", "/").Replace("\\", "/"));
										}
									}
								}
							}
						}
						catch {}
					}
					else if (File.Exists(catalogBinPath))
					{
						try
						{
							byte[] binData = File.ReadAllBytes(catalogBinPath);
							int index = 0;
							int len = binData.Length;
							while (index < len)
							{
								if (index + 7 < len &&
									binData[index] == 'A' &&
									binData[index + 1] == 's' &&
									binData[index + 2] == 's' &&
									binData[index + 3] == 'e' &&
									binData[index + 4] == 't' &&
									binData[index + 5] == 's' &&
									binData[index + 6] == '/')
								{
									int start = index;
									while (index < len && binData[index] >= 32 && binData[index] <= 126)
									{
										index++;
									}
									if (index > start)
									{
										string path = Encoding.ASCII.GetString(binData, start, index - start);
										internalIds.Add(path);
									}
								}
								else
								{
									index++;
								}
							}
						}
						catch {}
					}

					if (internalIds.Count == 0)
					{
						if (verbose) Debug.LogWarning("AssetRipper: Addressable catalog data not found or failed to parse.");
						return;
					}

					AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
					if (settings == null)
					{
						if (verbose) Debug.LogWarning("AssetRipper: Addressables must be initialized in this project first (Window -> Asset Management -> Addressables -> Groups).");
						return;
					}

					try
					{
						int registered = 0;
						AddressableAssetGroup defaultGroup = settings.DefaultGroup;

						foreach (string cleanPath in internalIds)
						{
							if (cleanPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
							{
								string guid = AssetDatabase.AssetPathToGUID(cleanPath);
								if (!string.IsNullOrEmpty(guid))
								{
									AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, defaultGroup, false, false);
									if (entry != null)
									{
										entry.address = cleanPath;
										registered++;
									}
								}
							}
						}

						if (registered > 0)
						{
							settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);
							AssetDatabase.SaveAssets();
							Debug.Log($"AssetRipper successfully reconstructed and registered {registered} Addressable assets inside the project groups.");
						}
					}
					catch (Exception ex)
					{
						Debug.LogError($"AssetRipper: Failed to reconstruct Addressables from catalog: {ex.Message}");
					}
				}

				private static void DiagnoseUnresolvedScripts()
				{
					string mapPath = GetAbsoluteMapPath();
					if (!File.Exists(mapPath))
					{
						Debug.LogWarning("AssetRipper relink map not found at: " + mapPath);
						return;
					}
					Dictionary<string, string> sourceMap = LoadSourceMap(mapPath);
					Dictionary<string, (string Guid, long FileID)> installedScripts = BuildInstalledScriptMap();
					HashSet<string> allIdentities = new HashSet<string>(sourceMap.Values);
					List<string> missing = new List<string>();
					List<string> found = new List<string>();
					foreach (string identity in allIdentities)
					{
						if (installedScripts.ContainsKey(identity))
						{
							found.Add(identity);
						}
						else
						{
							missing.Add(identity);
						}
					}
					StringBuilder sb = new StringBuilder();
					sb.AppendLine("=== AssetRipper Script Reference Diagnostic ===");
					sb.AppendLine("Total unique script identities in map: " + allIdentities.Count.ToString(CultureInfo.InvariantCulture));
					sb.AppendLine("Resolved (found in project): " + found.Count.ToString(CultureInfo.InvariantCulture));
					sb.AppendLine("Unresolved (missing): " + missing.Count.ToString(CultureInfo.InvariantCulture));
					if (missing.Count > 0)
					{
						sb.AppendLine();
						sb.AppendLine("Missing scripts (Assembly / FullType):");
						missing.Sort(StringComparer.Ordinal);
						foreach (string identity in missing)
						{
							sb.AppendLine("  - " + identity.Replace("|", " / "));
						}
					}
					if (missing.Count > 0)
					{
						Debug.LogWarning(sb.ToString());
					}
					else
					{
						Debug.Log(sb.ToString());
					}
				}

				private static bool TryRelinkFileFast(
					string assetPath,
					Dictionary<string, string> sourceMap,
					Dictionary<string, (string Guid, long FileID)> installedScripts,
					out string updatedText,
					out int replacements,
					out int skipped,
					out int unresolved,
					ConcurrentBag<string> unresolvedIdentities)
				{
					replacements = 0;
					skipped = 0;
					unresolved = 0;
					updatedText = string.Empty;

					string fullPath = Path.GetFullPath(assetPath);
					string originalText;
					try { originalText = File.ReadAllText(fullPath); }
					catch { return false; }

					if (!originalText.Contains("guid:") || !originalText.Contains("fileID:"))
					{
						return false;
					}

					int localReplacements = 0;
					int localSkipped = 0;
					int localUnresolved = 0;

					string resultText = PPtrReferenceRegex.Replace(originalText, match =>
					{
						if (!long.TryParse(match.Groups["fileID"].Value, NumberStyles.Integer,
								CultureInfo.InvariantCulture, out long fileId))
						{
							return match.Value;
						}
						string guid = match.Groups["guid"].Value.ToLowerInvariant();
						string sourceKey = MakeSourceKey(guid, fileId);
						if (!sourceMap.TryGetValue(sourceKey, out string identityKey))
						{
							return match.Value;
						}
						if (!installedScripts.TryGetValue(identityKey, out var targetScript))
						{
							localUnresolved++;
							if (unresolvedIdentities != null)
							{
								unresolvedIdentities.Add(identityKey);
							}
							return match.Value;
						}
						if (string.Equals(targetScript.Guid, guid, StringComparison.OrdinalIgnoreCase)
							&& fileId == targetScript.FileID)
						{
							localSkipped++;
							return match.Value;
						}
						localReplacements++;
						
						string typeValue = match.Groups["type"].Success ? match.Groups["type"].Value : "3";
						return "{fileID: "
							+ targetScript.FileID.ToString(CultureInfo.InvariantCulture)
							+ ", guid: " + targetScript.Guid
							+ ", type: " + typeValue
							+ "}";
					});

					replacements = localReplacements;
					skipped = localSkipped;
					unresolved = localUnresolved;

					if (localReplacements == 0 || string.Equals(originalText, resultText, StringComparison.Ordinal))
					{
						return false;
					}

					updatedText = resultText;
					return true;
				}

				private static Dictionary<string, string> LoadSourceMap(string mapPath)
				{
					Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
					foreach (string rawLine in File.ReadAllLines(mapPath))
					{
						string line = rawLine.Trim();
						if (line.Length == 0 || line[0] == '#') continue;
						string[] parts = rawLine.Split('\t');
						if (parts.Length < 6) continue;
						if (!long.TryParse(parts[1], NumberStyles.Integer,
								CultureInfo.InvariantCulture, out long fileId))
						{
							continue;
						}
						map[MakeSourceKey(parts[0], fileId)] = MakeIdentityKey(parts[2], parts[5]);
					}
					return map;
				}

				private static Dictionary<string, string> LoadIdentityToGuidMap(string mapPath)
				{
					Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
					foreach (string rawLine in File.ReadAllLines(mapPath))
					{
						string line = rawLine.Trim();
						if (line.Length == 0 || line[0] == '#') continue;
						string[] parts = rawLine.Split('\t');
						if (parts.Length < 6) continue;
						string guid = parts[0].Trim().ToLowerInvariant();
						string identityKey = MakeIdentityKey(parts[2], parts[5]);
						if (!map.ContainsKey(identityKey))
						{
							map[identityKey] = guid;
						}
					}
					return map;
				}

				private static Dictionary<string, (string Guid, long FileID)> BuildInstalledScriptMap()
				{
					var installedScripts = new Dictionary<string, (string Guid, long FileID)>(StringComparer.Ordinal);
					string[] guids = AssetDatabase.FindAssets("t:MonoScript");
					foreach (string guid in guids)
					{
						string path = AssetDatabase.GUIDToAssetPath(guid);
						MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
						if (monoScript == null) continue;
						
						long fileId = MonoScriptFileId;
						if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(monoScript, out string _, out long actualFileId))
						{
							fileId = actualFileId;
						}

						Type type = monoScript.GetClass();
						if (type != null)
						{
							string assemblyName = type.Assembly.GetName().Name ?? string.Empty;
							string fullTypeName = type.FullName ?? string.Empty;
							string key = MakeIdentityKey(assemblyName, fullTypeName);
							if (!installedScripts.ContainsKey(key))
							{
								installedScripts[key] = (guid.ToLowerInvariant(), fileId);
							}
						}
						else
						{
							string scriptName = monoScript.name;
							if (string.IsNullOrEmpty(scriptName)) continue;
							string dir = Path.GetDirectoryName(path) ?? string.Empty;
							dir = dir.Replace('\\', '/');
							string inferredAssembly = TryInferAssemblyName(dir);
							string inferredNamespace = InferNamespace(dir);
							string fullTypeName = string.IsNullOrEmpty(inferredNamespace) ? scriptName : inferredNamespace + "." + scriptName;
							string key = MakeIdentityKey(inferredAssembly, fullTypeName);
							if (!installedScripts.ContainsKey(key))
							{
								installedScripts[key] = (guid.ToLowerInvariant(), fileId);
							}
						}
					}
					return installedScripts;
				}

				private static string InferNamespace(string directory)
				{
					directory = directory.Replace('\\', '/');
					if (!directory.EndsWith("/")) directory += "/";
					int scriptsIdx = directory.IndexOf("/Scripts/", StringComparison.OrdinalIgnoreCase);
					if (scriptsIdx >= 0)
					{
						string sub = directory.Substring(scriptsIdx + 9);
						int nextSlash = sub.IndexOf('/');
						if (nextSlash >= 0)
						{
							return sub.Substring(nextSlash + 1).Trim('/').Replace('/', '.');
						}
						return "";
					}
					int pluginsIdx = directory.IndexOf("/Plugins/", StringComparison.OrdinalIgnoreCase);
					if (pluginsIdx >= 0)
					{
						string sub = directory.Substring(pluginsIdx + 9);
						int nextSlash = sub.IndexOf('/');
						if (nextSlash >= 0)
						{
							return sub.Substring(nextSlash + 1).Trim('/').Replace('/', '.');
						}
						return "";
					}
					return "";
				}

				private static string TryInferAssemblyName(string directory)
				{
					try
					{
						string dir = directory.Replace('\\', '/');
						if (!dir.EndsWith("/")) dir += "/";
						int scriptsIdx = dir.IndexOf("/Scripts/", StringComparison.OrdinalIgnoreCase);
						if (scriptsIdx >= 0)
						{
							string sub = dir.Substring(scriptsIdx + 9);
							int nextSlash = sub.IndexOf('/');
							if (nextSlash >= 0)
							{
								return sub.Substring(0, nextSlash);
							}
						}
						int pluginsIdx = dir.IndexOf("/Plugins/", StringComparison.OrdinalIgnoreCase);
						if (pluginsIdx >= 0)
						{
							string sub = dir.Substring(pluginsIdx + 9);
							int nextSlash = sub.IndexOf('/');
							if (nextSlash >= 0)
							{
								return sub.Substring(0, nextSlash);
							}
						}
						while (!string.IsNullOrEmpty(dir))
						{
							dir = dir.TrimEnd('/');
							if (!dir.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)
								&& !dir.StartsWith("Packages", StringComparison.OrdinalIgnoreCase))
							{
								break;
							}
							string[] asmdefFiles = Directory.GetFiles(dir, "*.asmdef", SearchOption.TopDirectoryOnly);
							if (asmdefFiles.Length > 0)
							{
								return Path.GetFileNameWithoutExtension(asmdefFiles[0]);
							}
							string parent = Path.GetDirectoryName(dir);
							if (string.IsNullOrEmpty(parent) || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase)) break;
							dir = parent;
						}
					}
					catch
					{
						// Silently fall back
					}
					return "Assembly-CSharp";
				}

				private static IEnumerable<string> EnumerateCandidateAssetPaths()
				{
					string[] extensions =
					{
						"*.prefab", "*.unity", "*.asset", "*.anim",
						"*.controller", "*.overrideController",
						"*.playable", "*.mat", "*.mask",
						"*.signal", "*.lighting", "*.flare",
						"*.mixer", "*.renderTexture",
						"*.shadervariants", "*.terrainlayer",
						"*.fontsettings", "*.guiskin", "*.brush",
						"*.physicMaterial", "*.physicsMaterial2D",
						"*.spriteatlas", "*.scenetemplate"
					};
					if (Directory.Exists("Assets"))
					{
						foreach (string extension in extensions)
						{
							foreach (string fullPath in Directory.GetFiles("Assets", extension, SearchOption.AllDirectories))
							{
								string normalizedPath = fullPath.Replace('\\', '/');
								if (normalizedPath.Contains("/Editor/AssetRipperPatches/")) continue;
								yield return normalizedPath;
							}
						}
					}
					if (Directory.Exists("Packages"))
					{
						foreach (string extension in extensions)
						{
							foreach (string fullPath in Directory.GetFiles("Packages", extension, SearchOption.AllDirectories))
							{
								yield return fullPath.Replace('\\', '/');
							}
						}
					}
				}

				private static string MakeSourceKey(string guid, long fileId)
				{
					return guid.ToLowerInvariant() + "|" + fileId.ToString(CultureInfo.InvariantCulture);
				}

				private static string MakeIdentityKey(string assemblyName, string fullTypeName)
				{
					return NormalizeAssemblyName(assemblyName) + "|" + fullTypeName;
				}

				private static string NormalizeAssemblyName(string assemblyName)
				{
					switch (assemblyName)
					{
						case "unity.addressables": return "Unity.Addressables";
						default: return assemblyName;
					}
				}

				private static string GetAbsoluteMapPath()
				{
					return Path.GetFullPath(MapRelativePath);
				}
			}
		}
		#endif
		""";
}