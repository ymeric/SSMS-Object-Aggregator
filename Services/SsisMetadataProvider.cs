using Microsoft.Data.SqlClient;
using SSMS.ObjectAggregator.Models;

namespace SSMS.ObjectAggregator.Services;

/// <summary>
/// Retrieves SSIS-related objects from a SQL Server instance:
/// SQL Server Agent jobs (from <c>msdb.dbo.sysjobs</c>) and Integration Services Catalog
/// packages (from <c>SSISDB.catalog.packages</c>). Each source is attempted independently —
/// if one is unavailable on the target instance it is silently skipped, so the provider
/// always returns whichever subset is accessible.
/// </summary>
public class SsisMetadataProvider : ISqlObjectMetadataProvider
{
    public async Task<IReadOnlyList<SqlObjectReference>> GetObjectsAsync(string instanceName, string databaseName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return Array.Empty<SqlObjectReference>();
        }

        var results = new List<SqlObjectReference>();

        await TryLoadAgentJobsAsync(instanceName, results, cancellationToken).ConfigureAwait(false);
        await TryLoadSsisPackagesAsync(instanceName, results, cancellationToken).ConfigureAwait(false);

        return results;
    }

    private static async Task TryLoadAgentJobsAsync(string instanceName, List<SqlObjectReference> results, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = instanceName,
                InitialCatalog = "msdb",
                IntegratedSecurity = true,
                TrustServerCertificate = true
            };

            using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string query = "SELECT name FROM msdb.dbo.sysjobs ORDER BY name";
            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new SqlObjectReference(
                    instanceName,
                    "SSIS",
                    string.Empty,
                    reader.GetString(0),
                    "SQL_AGENT_JOB"));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // SQL Server Agent not available on this instance — skip silently.
        }
    }

    private static async Task TryLoadSsisPackagesAsync(string instanceName, List<SqlObjectReference> results, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = instanceName,
                InitialCatalog = "SSISDB",
                IntegratedSecurity = true,
                TrustServerCertificate = true
            };

            using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            const string query = @"
SELECT
    f.name  AS FolderName,
    p.name  AS ProjectName,
    pk.name AS PackageName
FROM       SSISDB.catalog.packages  pk
INNER JOIN SSISDB.catalog.projects  p  ON p.project_id = pk.project_id
INNER JOIN SSISDB.catalog.folders   f  ON f.folder_id  = p.folder_id
ORDER BY f.name, p.name, pk.name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(new SqlObjectReference(
                    instanceName,
                    "SSIS",
                    reader.GetString(0),    // SchemaName       → folder name (used for SchemaFilter matching)
                    reader.GetString(2),    // ObjectName       → package name
                    "SSIS_PACKAGE",
                    null,
                    reader.GetString(1),    // ParentObjectName → project name
                    null));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // SSISDB catalog not available on this instance — skip silently.
        }
    }
}
