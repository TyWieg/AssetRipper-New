using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Classes.ClassID_117;
using AssetRipper.SourceGenerated.Classes.ClassID_27;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetRipper.Processing.Addressables;

public sealed class BinaryCatalogReader
{
	private const int Magic = 0x0de38942;
	private const int ExpectedVersion = 2;
	private const uint UnicodeStringFlag = 0x80000000;
	private const uint DynamicStringFlag = 0x40000000;
	private const uint ClearFlagsMask = 0x3fffffff;

	private readonly byte[] buffer;

	public BinaryCatalogReader(byte[] buffer)
	{
		this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
	}

	public List<CompactCatalogEntry> Parse()
	{
		List<CompactCatalogEntry> entries = new();
		if (buffer.Length < 32)
		{
			return entries;
		}

		// Read Header
		int magic = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0, 4));
		int version = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4, 4));

		if (magic != Magic)
		{
			Logger.Warning(LogCategory.Processing, "Invalid binary catalog magic number. Skipping parse.");
			return entries;
		}

		if (version != ExpectedVersion)
		{
			Logger.Warning(LogCategory.Processing, $"Unsupported binary catalog version: {version}. Expected: {ExpectedVersion}. Skipping parse.");
			return entries;
		}

		uint keysOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8, 4));
		uint idOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12, 4));

		// Parse key table
		if (keysOffset < 4 || keysOffset >= buffer.Length)
		{
			return entries;
		}

		uint keysSizeBytes = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)keysOffset - 4, 4));
		int keyCount = (int)(keysSizeBytes / 8); // sizeof(uint) * 2 per KeyData

		// Prevent index overflows if header is corrupt or malicious
		if (keysOffset + (keyCount * 8) > (uint)buffer.Length)
		{
			keyCount = (int)((buffer.Length - keysOffset) / 8);
		}

		Dictionary<uint, string> keyOffsetToValue = new();
		Dictionary<uint, List<string>> locationOffsetToKeys = new();

		for (int i = 0; i < keyCount; i++)
		{
			int offset = (int)keysOffset + (i * 8);
			uint keyNameOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
			uint locationSetOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 4, 4));

			string keyName = ReadString(keyNameOffset);
			keyOffsetToValue[keyNameOffset] = keyName;

			if (locationSetOffset != uint.MaxValue && !string.IsNullOrEmpty(keyName))
			{
				List<uint> locOffsets = ReadObjectArrayOffsets(locationSetOffset);
				foreach (uint locOffset in locOffsets)
				{
					if (!locationOffsetToKeys.TryGetValue(locOffset, out List<string>? associatedKeys))
					{
						associatedKeys = new();
						locationOffsetToKeys[locOffset] = associatedKeys;
					}
					if (!associatedKeys.Contains(keyName))
					{
						associatedKeys.Add(keyName);
					}
				}
			}
		}

		// Rebuild Compact Entries using O(1) reverse lookup
		foreach (var kvp in locationOffsetToKeys)
		{
			uint locOffset = kvp.Key;
			List<string> associatedKeys = kvp.Value;

			CompactCatalogEntry? entry = ParseResourceLocation(locOffset);
			if (entry != null)
			{
				foreach (string key in associatedKeys)
				{
					if (!entry.Keys.Contains(key))
					{
						entry.Keys.Add(key);
					}
				}
				entries.Add(entry);
			}
		}

		return entries;
	}

	private CompactCatalogEntry? ParseResourceLocation(uint offset)
	{
		if (offset == uint.MaxValue || offset >= buffer.Length)
		{
			return null;
		}

		int baseOffset = (int)offset;
		if (baseOffset + 28 > buffer.Length)
		{
			return null;
		}

		uint primaryKeyOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(baseOffset, 4));
		uint internalIdOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(baseOffset + 4, 4));
		uint providerOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(baseOffset + 8, 4));
		uint dependencySetOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(baseOffset + 12, 4));
		uint extraDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(baseOffset + 20, 4));
		uint typeId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(baseOffset + 24, 4));

		string primaryKey = ReadString(primaryKeyOffset);
		string internalId = ReadString(internalIdOffset);
		string providerId = ReadString(providerOffset);

		Type resourceType = ResolveType(typeId);

		CompactCatalogEntry entry = new()
		{
			PrimaryKey = primaryKey,
			InternalId = internalId,
			ProviderId = providerId,
			ResourceType = resourceType
		};

		if (dependencySetOffset != uint.MaxValue)
		{
			List<uint> depOffsets = ReadObjectArrayOffsets(dependencySetOffset);
			foreach (uint depOffset in depOffsets)
			{
				string depKey = ReadString(depOffset);
				if (!string.IsNullOrEmpty(depKey))
				{
					entry.Dependencies.Add(depKey);
				}
			}
		}

		return entry;
	}

	private List<uint> ReadObjectArrayOffsets(uint offset)
	{
		List<uint> offsets = new();
		if (offset < 4 || offset >= buffer.Length)
		{
			return offsets;
		}

		uint sizeBytes = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)offset - 4, 4));
		int count = (int)(sizeBytes / 4);

		if ((int)offset + (count * 4) > buffer.Length)
		{
			count = (int)((buffer.Length - offset) / 4);
		}

		for (int i = 0; i < count; i++)
		{
			uint elementOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)offset + (i * 4), 4));
			if (elementOffset != uint.MaxValue)
			{
				offsets.Add(elementOffset);
			}
		}

		return offsets;
	}

	private string ReadString(uint id)
	{
		if (id == uint.MaxValue)
		{
			return string.Empty;
		}

		if ((id & DynamicStringFlag) == DynamicStringFlag)
		{
			return ReadDynamicString(id);
		}

		return ReadAutoEncodedString(id);
	}

	private string ReadAutoEncodedString(uint id)
	{
		uint cleanId = id & ClearFlagsMask;
		if (cleanId < 4 || cleanId >= buffer.Length)
		{
			return string.Empty;
		}

		uint lengthBytes = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)cleanId - 4, 4));
		if (cleanId + lengthBytes > buffer.Length)
		{
			return string.Empty;
		}

		bool isUnicode = (id & UnicodeStringFlag) == UnicodeStringFlag;
		Encoding encoding = isUnicode ? Encoding.Unicode : Encoding.ASCII;

		return encoding.GetString(buffer, (int)cleanId, (int)lengthBytes);
	}

	private string ReadDynamicString(uint id)
	{
		StringBuilder builder = new();
		uint nextId = id;
		HashSet<uint> visited = new();

		while (nextId != uint.MaxValue && visited.Add(nextId))
		{
			uint cleanId = nextId & ClearFlagsMask;
			if (cleanId + 8 > buffer.Length)
			{
				break;
			}

			uint stringId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)cleanId, 4));
			nextId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)cleanId + 4, 4));

			string part = ReadAutoEncodedString(stringId);
			builder.Append(part);

			if (nextId != uint.MaxValue)
			{
				builder.Append('/'); // Standard path separator for dynamic strings
			}
		}

		return builder.ToString();
	}

	private Type ResolveType(uint offset)
	{
		if (offset == uint.MaxValue || offset + 8 > buffer.Length)
		{
			return typeof(object);
		}

		uint assemblyId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)offset, 4));
		uint classId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)offset + 4, 4));

		string className = ReadString(classId);

		return className switch
		{
			"UnityEngine.GameObject" => typeof(GameObject),
			"UnityEngine.Sprite" => typeof(Sprite),
			"UnityEngine.Texture" => typeof(Texture),
			"UnityEngine.Texture2D" => typeof(Texture2D),
			"UnityEngine.Texture3D" => typeof(Texture3D),
			_ => typeof(object)
		};
	}
}