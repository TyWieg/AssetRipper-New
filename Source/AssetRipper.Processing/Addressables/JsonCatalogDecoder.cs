using AssetRipper.Import.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace AssetRipper.Processing.Addressables;

public static class JsonCatalogDecoder
{
	private const int BytesPerInt32 = 4;
	private const int EntryDataItemCount = 7;

	public static List<CompactCatalogEntry> Decode(
		string[] providerIds,
		string[] internalIds,
		string keyDataStr,
		string bucketDataStr,
		string entryDataStr,
		string extraDataStr)
	{
		List<CompactCatalogEntry> entries = new();

		try
		{
			byte[] keyData = Convert.FromBase64String(keyDataStr);
			byte[] bucketData = Convert.FromBase64String(bucketDataStr);
			byte[] entryData = Convert.FromBase64String(entryDataStr);

			if (keyData.Length < 4 || bucketData.Length < 4 || entryData.Length < 4)
			{
				return entries;
			}

			int keyCount = BinaryPrimitives.ReadInt32LittleEndian(keyData.AsSpan(0, 4));
			
			// Sanity check buffer size to prevent out-of-memory attack vectors on heap
			if (keyCount <= 0 || keyCount > 1000000)
			{
				return entries;
			}

			object[] keys = new object[keyCount];

			// Deserialize keys
			int keyOffset = 4;
			for (int i = 0; i < keyCount; i++)
			{
				if (keyOffset >= keyData.Length) break;
				byte keyType = keyData[keyOffset];
				keyOffset++;

				switch (keyType)
				{
					case 0: // AsciiString
						if (keyOffset + 4 > keyData.Length) break;
						int asciiLen = BinaryPrimitives.ReadInt32LittleEndian(keyData.AsSpan(keyOffset, 4));
						keyOffset += 4;
						if (keyOffset + asciiLen > keyData.Length || asciiLen < 0) break;
						keys[i] = Encoding.ASCII.GetString(keyData, keyOffset, asciiLen);
						keyOffset += asciiLen;
						break;

					case 1: // UnicodeString
						if (keyOffset + 4 > keyData.Length) break;
						int uniLen = BinaryPrimitives.ReadInt32LittleEndian(keyData.AsSpan(keyOffset, 4));
						keyOffset += 4;
						if (keyOffset + uniLen > keyData.Length || uniLen < 0) break;
						keys[i] = Encoding.Unicode.GetString(keyData, keyOffset, uniLen);
						keyOffset += uniLen;
						break;

					case 4: // Int32
						if (keyOffset + 4 > keyData.Length) break;
						keys[i] = ReadInt32KeepArgs(keyData, keyOffset);
						keyOffset += 4;
						break;

					default:
						keys[i] = string.Empty;
						break;
				}
			}

			// Deserialize buckets
			int bucketCount = ReadInt32KeepArgs(bucketData, 0);
			if (bucketCount <= 0 || bucketCount > 1000000)
			{
				return entries;
			}

			int bucketOffset = 4;

			Dictionary<int, int[]> bucketMap = new();
			for (int i = 0; i < bucketCount; i++)
			{
				if (bucketOffset + 8 > bucketData.Length) break;
				int dataOffset = ReadInt32KeepArgs(bucketData, bucketOffset);
				bucketOffset += 4;
				int entryCount = ReadInt32KeepArgs(bucketData, bucketOffset);
				bucketOffset += 4;

				if (entryCount < 0 || entryCount > 100000) break;

				int[] entryArray = new int[entryCount];
				for (int c = 0; c < entryCount; c++)
				{
					if (bucketOffset + 4 > bucketData.Length) break;
					entryArray[c] = ReadInt32KeepArgs(bucketData, bucketOffset);
					bucketOffset += 4;
				}

				bucketMap[i] = entryArray;
			}

			// Map keys to entries via O(1) reverse indexing lookup
			Dictionary<int, List<string>> entryIndexToKeys = new();
			for (int i = 0; i < bucketCount; i++)
			{
				if (bucketMap.TryGetValue(i, out int[]? entryIndices))
				{
					string keyName = keys[i]?.ToString() ?? "";
					if (string.IsNullOrEmpty(keyName)) continue;

					foreach (int entryIndex in entryIndices)
					{
						if (!entryIndexToKeys.TryGetValue(entryIndex, out List<string>? associatedKeys))
						{
							associatedKeys = new();
							entryIndexToKeys[entryIndex] = associatedKeys;
						}
						if (!associatedKeys.Contains(keyName))
						{
							associatedKeys.Add(keyName);
						}
					}
				}
			}

			// Deserialize entries
			int entryCountTotal = ReadInt32KeepArgs(entryData, 0);
			if (entryCountTotal <= 0 || entryCountTotal > 1000000)
			{
				return entries;
			}

			for (int i = 0; i < entryCountTotal; i++)
			{
				int index = BytesPerInt32 + (i * (BytesPerInt32 * EntryDataItemCount));
				if (index + (BytesPerInt32 * EntryDataItemCount) > entryData.Length) break;

				int internalIdIndex = ReadInt32KeepArgs(entryData, index);
				int providerIndex = ReadInt32KeepArgs(entryData, index + 4);
				int dependencyKeyIndex = ReadInt32KeepArgs(entryData, index + 8);
				int primaryKeyIndex = ReadInt32KeepArgs(entryData, index + 20);

				if (internalIdIndex < 0 || internalIdIndex >= internalIds.Length ||
					providerIndex < 0 || providerIndex >= providerIds.Length)
				{
					continue;
				}

				string internalId = internalIds[internalIdIndex];
				string providerId = providerIds[providerIndex];
				string primaryKey = primaryKeyIndex >= 0 && primaryKeyIndex < keys.Length ? keys[primaryKeyIndex]?.ToString() ?? "" : "";

				CompactCatalogEntry entry = new()
				{
					PrimaryKey = primaryKey,
					InternalId = internalId,
					ProviderId = providerId,
					ResourceType = typeof(object)
				};

				if (dependencyKeyIndex >= 0 && dependencyKeyIndex < keys.Length)
				{
					string depKey = keys[dependencyKeyIndex]?.ToString() ?? "";
					if (!string.IsNullOrEmpty(depKey))
					{
						entry.Dependencies.Add(depKey);
					}
				}

				// Assign alternative keys and labels from reverse lookup index
				if (entryIndexToKeys.TryGetValue(i, out List<string>? associatedKeys))
				{
					foreach (string key in associatedKeys)
					{
						if (!entry.Keys.Contains(key))
						{
							entry.Keys.Add(key);
						}
					}
				}

				entries.Add(entry);
			}
		}
		catch (Exception ex)
		{
			Logger.Error(LogCategory.Processing, $"Failed to decode JSON Catalog: {ex.Message}");
		}

		return entries;
	}

	private static int ReadInt32KeepArgs(byte[] data, int offset)
	{
		return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
	}
}