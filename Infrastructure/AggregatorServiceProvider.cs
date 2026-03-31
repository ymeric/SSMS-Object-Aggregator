using SSMS.ObjectAggregator.Services;

namespace SSMS.ObjectAggregator.Infrastructure;

public interface IAggregatorServices
{
    #region Properties

    GroupStorageService StorageService { get; }
    GroupReloadService ReloadService { get; }

    #endregion Properties
}

public static class AggregatorServiceProvider
{
    #region State

    private static readonly object SyncRoot = new();
    private static IAggregatorServices? _services;

    #endregion State

    #region Properties

    public static IAggregatorServices Services => _services ?? throw new InvalidOperationException("Aggregator services have not been initialized.");

    public static IServiceProvider? ShellServiceProvider { get; private set; }

    #endregion Properties

    #region Initialization

    public static void InitializeShell(IServiceProvider serviceProvider)
        => ShellServiceProvider = serviceProvider;

    public static void Initialize(IAggregatorServices services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        lock (SyncRoot)
        {
            _services = services;
        }
    }

    public static void EnsureInitialized(Func<IAggregatorServices> factory)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (_services is not null)
        {
            return;
        }

        lock (SyncRoot)
        {
            _services ??= factory();
        }
    }

    #endregion Initialization
}