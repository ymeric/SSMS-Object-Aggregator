using SSMS.ObjectAggregator.Infrastructure;
using System.Windows;

namespace SSMS.ObjectAggregator;

public partial class App : Application
{
    public App()
    {
        AggregatorServiceProvider.EnsureInitialized(() => new DefaultAggregatorServices());
    }
}