using SSMS.ObjectAggregator.Models;

namespace SSMS.ObjectAggregator.Services;

public class GroupReloadService
{
    private readonly ISqlObjectMetadataProvider _metadataProvider;
    private readonly ISqlObjectMetadataProvider _ssisMetadataProvider;

    public GroupReloadService(ISqlObjectMetadataProvider metadataProvider)
        : this(metadataProvider, new SsisMetadataProvider()) { }

    public GroupReloadService(ISqlObjectMetadataProvider metadataProvider, ISqlObjectMetadataProvider ssisMetadataProvider)
    {
        _metadataProvider = metadataProvider;
        _ssisMetadataProvider = ssisMetadataProvider;
    }

    public async Task<IReadOnlyList<SqlObjectReference>> ReloadAsync(GroupDefinition group, CancellationToken cancellationToken)
    {
        if (group is null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        var collected = new List<SqlObjectReference>();

        foreach (var criterion in group.Criteria)
        {
            if (string.IsNullOrWhiteSpace(criterion.InstanceName) || string.IsNullOrWhiteSpace(criterion.DatabaseName))
            {
                continue;
            }

            var provider = criterion.IsSsisInstance ? _ssisMetadataProvider : _metadataProvider;
            var objects = await provider.GetObjectsAsync(criterion.InstanceName, criterion.DatabaseName, cancellationToken).ConfigureAwait(false);
            List<FilterDefinition>? filters = criterion.Filters;

            if (filters == null || filters.Count == 0)
            {
                collected.AddRange(objects);
                continue;
            }

            foreach (var filter in filters)
            {
                collected.AddRange(objects.Where(o =>
                    FilterPatternMatcher.IsMatch(filter.SchemaFilter, o.SchemaName) &&
                    FilterPatternMatcher.IsMatch(filter.ObjectFilter, o.ObjectName)));
            }
        }

        return collected
            .Distinct()
            .ToList();
    }
}