namespace AssetRipper.Export.UnityProjects.Addressables;

internal sealed class AddressablesSettingsData
{
	public string? m_buildTarget { get; set; }
	public string? m_SettingsHash { get; set; }
	public string? m_AddressablesVersion { get; set; }
	public int m_maxConcurrentWebRequests { get; set; }
	public int m_CatalogRequestsTimeout { get; set; }
	public bool m_IsLocalCatalogInBundle { get; set; }
	public bool m_DisableCatalogUpdateOnStart { get; set; }
}