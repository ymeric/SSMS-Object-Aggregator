using SSMS.ObjectAggregator.Models;

namespace SSMS.ObjectAggregator.ViewModels;

public class SqlObjectNodeViewModel
{
    public SqlObjectNodeViewModel(GroupViewModel parent, SqlObjectReference reference)
    {
        ParentGroup = parent;
        Reference = reference;
    }

    public GroupViewModel ParentGroup { get; }

    public SqlObjectReference Reference { get; }

    public string ObjectType => Reference.ObjectType;

    public string DisplayName
    {
        get
        {
            string schemaPrefix = string.IsNullOrEmpty(Reference.SchemaName)
                ? string.Empty
                : $"{Reference.SchemaName}.";

            string baseName = $"{schemaPrefix}{Reference.ObjectName}";

            if (!string.IsNullOrEmpty(Reference.ParentObjectName))
            {
                string parentPrefix = string.IsNullOrEmpty(Reference.ParentSchemaName)
                    ? string.Empty
                    : $"{Reference.ParentSchemaName}.";
                return $"{baseName} (on {parentPrefix}{Reference.ParentObjectName})";
            }

            return baseName;
        }
    }
}