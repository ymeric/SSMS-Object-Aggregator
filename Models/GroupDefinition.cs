namespace SSMS.ObjectAggregator.Models;

public class GroupDefinition
{
    #region Properties

    public string Name { get; set; } = string.Empty;
    public List<GroupCriterion> Criteria { get; set; } = new();

    #endregion Properties
}

public class GroupCriterion
{
    #region Properties

    public string InstanceName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public List<FilterDefinition> Filters { get; set; } = new();

    /// <summary>
    /// Returns <see langword="true"/> when <see cref="DatabaseName"/> is the sentinel value
    /// <c>"SSIS"</c>, indicating that this criterion targets an SSIS server instance.
    /// Objects are fetched from SQL Server Agent (<c>msdb.dbo.sysjobs</c>) and/or the
    /// Integration Services Catalog (<c>SSISDB.catalog.packages</c>), whichever is available.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSsisInstance =>
        string.Equals(DatabaseName, "SSIS", StringComparison.OrdinalIgnoreCase);

    #endregion Properties
}

public class FilterDefinition
{
    #region Properties

    public string? SchemaFilter { get; set; }
    public string? ObjectFilter { get; set; }

    #endregion Properties
}