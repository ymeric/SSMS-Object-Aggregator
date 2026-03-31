namespace SSMS.ObjectAggregator.Models;

public record SqlObjectReference(
    string InstanceName,
    string DatabaseName,
    string SchemaName,
    string ObjectName,
    string ObjectType,
    string? ParentSchemaName = null,
    string? ParentObjectName = null,
    string? ParentObjectType = null);