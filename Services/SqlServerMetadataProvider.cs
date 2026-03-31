using Microsoft.Data.SqlClient;
using SSMS.ObjectAggregator.Models;
using System.Runtime.InteropServices;

namespace SSMS.ObjectAggregator.Services;

public class SqlServerMetadataProvider : ISqlObjectMetadataProvider
{
    public async Task<IReadOnlyList<SqlObjectReference>> GetObjectsAsync(string instanceName, string databaseName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceName) || string.IsNullOrWhiteSpace(databaseName))
        {
            return Array.Empty<SqlObjectReference>();
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = instanceName,
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true
        };

        using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string query = @"
SELECT
    s.name            AS SchemaName,
    o.name            AS ObjectName,
    o.type_desc       AS ObjectType,
    ps.name           AS ParentSchemaName,
    po.name           AS ParentObjectName,
    po.type_desc      AS ParentObjectType
FROM       sys.objects o
INNER JOIN sys.schemas s  ON s.schema_id  = o.schema_id
LEFT JOIN  sys.objects po ON po.object_id = o.parent_object_id
LEFT JOIN  sys.schemas ps ON ps.schema_id = po.schema_id
WHERE o.is_ms_shipped = 0

UNION ALL

-- Database-level DDL triggers (not present in sys.objects)
SELECT
    ''                     AS SchemaName,
    t.name                 AS ObjectName,
    'DATABASE_DDL_TRIGGER' AS ObjectType,
    NULL                   AS ParentSchemaName,
    NULL                   AS ParentObjectName,
    NULL                   AS ParentObjectType
FROM sys.triggers t
WHERE t.parent_class  = 0
  AND t.is_ms_shipped = 0

UNION ALL

-- User-defined scalar data types (not exposed via sys.objects)
SELECT
    s.name              AS SchemaName,
    tp.name             AS ObjectName,
    'USER_DEFINED_TYPE' AS ObjectType,
    NULL                AS ParentSchemaName,
    NULL                AS ParentObjectName,
    NULL                AS ParentObjectType
FROM       sys.types   tp
INNER JOIN sys.schemas s  ON s.schema_id = tp.schema_id
WHERE tp.is_user_defined = 1
  AND tp.is_table_type   = 0

UNION ALL

-- XML schema collections
SELECT
    s.name                  AS SchemaName,
    xsc.name                AS ObjectName,
    'XML_SCHEMA_COLLECTION' AS ObjectType,
    NULL                    AS ParentSchemaName,
    NULL                    AS ParentObjectName,
    NULL                    AS ParentObjectType
FROM       sys.xml_schema_collections xsc
INNER JOIN sys.schemas s ON s.schema_id = xsc.schema_id
WHERE s.name <> N'sys'";

        using var command = new SqlCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var items = new List<SqlObjectReference>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                string? parentSchemaName = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3);
                string? parentObjectName = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4);
                string? parentObjectType = await reader.IsDBNullAsync(5, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(5);
                items.Add(new SqlObjectReference(
                    instanceName,
                    databaseName,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    parentSchemaName,
                    parentObjectName,
                    parentObjectType));
            }catch(Exception ex)
            {
                var a = ex;
            }
        }

        return items;
    }
}