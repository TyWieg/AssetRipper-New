using AssetRipper.Assets;
using AssetRipper.Assets.Cloning;
using AssetRipper.Assets.IO.Writing;
using AssetRipper.Assets.Metadata;
using AssetRipper.Assets.Traversal;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using System;
using System.Collections.Generic;

namespace AssetRipper.Import.Structure.Assembly.Serializable;

public sealed class SerializablePair : UnityAssetBase
{
	public int Depth { get; }
	public SerializableType Type { get; }
	public SerializableValue First { get; set; }
	public SerializableValue Second { get; set; }
	public SerializableType.Field FirstField { get; }
	public SerializableType.Field SecondField { get; }

	public SerializablePair(SerializableType type, int depth)
	{
		Depth = depth;
		Type = type ?? throw new ArgumentNullException(nameof(type));
		FirstField = type.Fields[0];
		SecondField = type.Fields[1];
	}

	public void Initialize(UnityVersion version)
	{
		First.Initialize(version, Depth, FirstField);
		Second.Initialize(version, Depth, SecondField);
	}

	public void Read(ref EndianSpanReader reader, UnityVersion version, TransferInstructionFlags flags)
	{
		Read(ref reader, version, flags, null);
	}

	internal void Read(ref EndianSpanReader reader, UnityVersion version, TransferInstructionFlags flags, ManagedReferenceResolver? managedReferenceResolver)
	{
		First.Read(ref reader, version, flags, Depth, FirstField, managedReferenceResolver);
		Second.Read(ref reader, version, flags, Depth, SecondField, managedReferenceResolver);
	}

	public void Write(AssetWriter writer)
	{
		First.Write(writer, FirstField);
		Second.Write(writer, SecondField);
	}

	public override void WriteEditor(AssetWriter writer) => Write(writer);
	public override void WriteRelease(AssetWriter writer) => Write(writer);

	public override void WalkEditor(AssetWalker walker)
	{
		if (walker.EnterAsset(this))
		{
			if (walker.EnterField(this, FirstField.Name))
			{
				First.WalkEditor(walker, FirstField);
				walker.ExitField(this, FirstField.Name);
			}
			walker.DivideAsset(this);
			if (walker.EnterField(this, SecondField.Name))
			{
				Second.WalkEditor(walker, SecondField);
				walker.ExitField(this, SecondField.Name);
			}
			walker.ExitAsset(this);
		}
	}

	public override void WalkRelease(AssetWalker walker) => WalkEditor(walker);
	public override void WalkStandard(AssetWalker walker) => WalkEditor(walker);

	public override IEnumerable<(string, PPtr)> FetchDependencies()
	{
		foreach ((string path, PPtr pptr) in First.FetchDependencies(FirstField))
		{
			yield return ($"first.{path}", pptr);
		}
		foreach ((string path, PPtr pptr) in Second.FetchDependencies(SecondField))
		{
			yield return ($"second.{path}", pptr);
		}
	}

	public override void CopyValues(IUnityAssetBase? source, PPtrConverter converter)
	{
		if (source is not SerializablePair other)
		{
			Reset();
			return;
		}
		First.CopyValues(other.First, Depth, FirstField, converter);
		Second.CopyValues(other.Second, Depth, SecondField, converter);
	}

	public override void Reset()
	{
		First.Reset();
		Second.Reset();
	}
}