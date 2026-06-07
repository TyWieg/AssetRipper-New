using AssetRipper.Assets;
using AssetRipper.Assets.Metadata;
using AssetRipper.SourceGenerated.Subclasses.SceneObjectIdentifier;
using AssetRipper.Yaml;
using System;

namespace AssetRipper.Export.UnityProjects.Project;

public sealed class ProjectYamlWalker : YamlWalker
{
	private readonly IExportContainer container;

	public ProjectYamlWalker(IExportContainer container)
	{
		this.container = container;
		WithUnityVersion(container.ExportVersion);
	}

	public IUnityObjectBase CurrentAsset { get; set; } = null!;

	public YamlDocument ExportYamlDocument(IUnityObjectBase asset)
	{
		CurrentAsset = asset;
		return ExportYamlDocument(asset, container.GetExportID(asset));
	}

	public YamlNode ExportYamlNode(IUnityObjectBase asset)
	{
		CurrentAsset = asset;
		return base.ExportYamlNode(asset);
	}

	public override bool EnterAsset(IUnityAssetBase asset)
	{
		if (asset is SceneObjectIdentifier sceneObjectIdentifier)
		{
			long targetObject = sceneObjectIdentifier.TargetObjectReference is not null
				? container.CreateExportPointer(sceneObjectIdentifier.TargetObjectReference).FileID
				: sceneObjectIdentifier.TargetObject;
			long targetPrefab = sceneObjectIdentifier.TargetPrefabReference is not null
				? container.CreateExportPointer(sceneObjectIdentifier.TargetPrefabReference).FileID
				: sceneObjectIdentifier.TargetPrefab;
			YamlMappingNode yamlMappingNode = new()
			{
				{ YamlScalarNode.Create("targetObject"), targetObject },
				{ YamlScalarNode.Create("targetPrefab"), targetPrefab },
			};
			AddNode(yamlMappingNode);
			return false;
		}
		else
		{
			return base.EnterAsset(asset);
		}
	}

	public override YamlNode CreateYamlNodeForPPtr<TAsset>(PPtr<TAsset> pptr)
	{
		if (pptr.PathID == 0)
		{
			return MetaPtr.NullPtr.ExportYaml(container.ExportVersion);
		}
		else if (CurrentAsset.Collection.TryGetAsset(pptr, out TAsset? asset))
		{
			return container.CreateExportPointer(asset).ExportYaml(container.ExportVersion);
		}
		else
		{
			AssetType assetType = container.ToExportType(typeof(TAsset));
			MetaPtr pointer = MetaPtr.CreateMissingReference(GetClassID(typeof(TAsset)), assetType);
			return pointer.ExportYaml(container.ExportVersion);
		}
	}

	public override void VisitPrimitive<T>(T value)
	{
		// Intercept and translate string-based AssetReference GUID fields inline
		if (value is string originalGuidStr && originalGuidStr.Length == 32 && IsGuidField(CurrentFieldName))
		{
			if (container is ProjectAssetContainer projectContainer && projectContainer.TryGetTranslatedGuid(originalGuidStr, out string? translatedGuid))
			{
				base.VisitPrimitive((T)(object)translatedGuid);
				return;
			}
		}

		base.VisitPrimitive(value);
	}

	private static bool IsGuidField(string? fieldName)
	{
		if (fieldName == null)
		{
			return false;
		}

		return fieldName.EndsWith("GUID", StringComparison.OrdinalIgnoreCase) ||
			   fieldName.EndsWith("Guid", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(fieldName, "guid", StringComparison.OrdinalIgnoreCase);
	}
}