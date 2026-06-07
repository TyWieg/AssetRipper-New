using System;
using System.Collections.Generic;

namespace AssetRipper.Processing.Addressables;

public sealed class CompactCatalogEntry
{
	public required string PrimaryKey { get; set; }
	public required string InternalId { get; set; }
	public required string ProviderId { get; set; }
	public required Type ResourceType { get; set; }
	public List<string> Keys { get; } = new();
	public List<string> Dependencies { get; } = new();
	public object? ExtraData { get; set; }
}