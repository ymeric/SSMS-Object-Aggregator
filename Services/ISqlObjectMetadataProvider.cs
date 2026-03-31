using SSMS.ObjectAggregator.Models;

namespace SSMS.ObjectAggregator.Services;

public interface ISqlObjectMetadataProvider
{
    Task<IReadOnlyList<SqlObjectReference>> GetObjectsAsync(string instanceName, string databaseName, CancellationToken cancellationToken);
}