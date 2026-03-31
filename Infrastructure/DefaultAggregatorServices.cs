using SSMS.ObjectAggregator.Services;

namespace SSMS.ObjectAggregator.Infrastructure;

public sealed class DefaultAggregatorServices : IAggregatorServices
{
    #region Construction

    public DefaultAggregatorServices()
        : this(new SqlServerMetadataProvider())
    {
    }

    public DefaultAggregatorServices(ISqlObjectMetadataProvider metadataProvider)
    {
        StorageService = new GroupStorageService();
        ReloadService = new GroupReloadService(metadataProvider);
    }

    #endregion Construction

    #region Properties

    public GroupStorageService StorageService { get; }

    public GroupReloadService ReloadService { get; }

    #endregion Properties
}