using SSMS.ObjectAggregator.Models;
using System.Collections.ObjectModel;

namespace SSMS.ObjectAggregator.ViewModels;

public class GroupViewModel : ViewModelBase
{
    private bool _isEditing;
    private string _editableName = string.Empty;
    private bool _hasLoadedObjects;
    private bool _isReloading;
    private string _statusMessage = string.Empty;

    public GroupViewModel(GroupDefinition model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        ObjectNodes = new ObservableCollection<object>();
        ShowPendingPlaceholder();
    }

    public GroupDefinition Model { get; }

    public ObservableCollection<object> ObjectNodes { get; }

    public string Name => Model.Name;

    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    public string EditableName
    {
        get => _editableName;
        set => SetProperty(ref _editableName, value);
    }

    public bool HasLoadedObjects
    {
        get => _hasLoadedObjects;
        private set => SetProperty(ref _hasLoadedObjects, value);
    }

    public bool IsReloading
    {
        get => _isReloading;
        set => SetProperty(ref _isReloading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public void BeginRename()
    {
        EditableName = Name;
        IsEditing = true;
    }

    public void CancelRename()
    {
        EditableName = Name;
        IsEditing = false;
    }

    public void CommitRename(string newName)
    {
        Model.Name = newName;
        OnPropertyChanged(nameof(Name));
        IsEditing = false;
    }

    public void ShowLoadingState()
    {
        ObjectNodes.Clear();
        ObjectNodes.Add(new PlaceholderNodeViewModel(this, "Loading objects..."));
        HasLoadedObjects = false;
    }

    public void SetObjects(IEnumerable<SqlObjectReference> objects)
    {
        ObjectNodes.Clear();
        var materialized = objects?.ToList() ?? new List<SqlObjectReference>();
        int count = materialized.Count;

        if (count == 0)
        {
            ObjectNodes.Add(new PlaceholderNodeViewModel(this, "No objects matched the current filters."));
            HasLoadedObjects = true;
            StatusMessage = "No matches.";
            return;
        }

        var perPair = materialized
            .GroupBy(o => (Instance: o.InstanceName ?? string.Empty, Database: o.DatabaseName ?? string.Empty), InstanceDatabaseComparer.Instance)
            .OrderBy(g => g.Key.Instance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Database, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in perPair)
        {
            var instanceNode = new InstanceDatabaseNodeViewModel(this, pair.Key.Instance, pair.Key.Database);
            var byType = pair
                .GroupBy(o => o.ObjectType ?? string.Empty)
                .Select(g => new ObjectTypeGroupViewModel(instanceNode, g.Key))
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var typeLookup = pair
                .GroupBy(o => o.ObjectType ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.OrderBy(o => o.SchemaName).ThenBy(o => o.ObjectName).ToList());

            foreach (var type in byType)
            {
                if (typeLookup.TryGetValue(type.ObjectTypeKey, out var entries))
                {
                    foreach (var entry in entries)
                    {
                        type.Children.Add(new SqlObjectNodeViewModel(this, entry));
                    }
                }

                instanceNode.TypeGroups.Add(type);
            }

            ObjectNodes.Add(instanceNode);
        }

        HasLoadedObjects = true;
        StatusMessage = $"Loaded {count} object(s).";
    }

    public void MarkObjectsStale()
    {
        HasLoadedObjects = false;
        ShowPendingPlaceholder();
    }

    private void ShowPendingPlaceholder()
    {
        ObjectNodes.Clear();
        ObjectNodes.Add(new PlaceholderNodeViewModel(this, "Expand to load objects."));
    }

    private sealed class InstanceDatabaseComparer : IEqualityComparer<(string Instance, string Database)>
    {
        public static InstanceDatabaseComparer Instance { get; } = new();

        public bool Equals((string Instance, string Database) x, (string Instance, string Database) y)
        {
            return string.Equals(x.Instance, y.Instance, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Database, y.Database, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Instance, string Database) obj)
        {
            unchecked
            {
                int instanceHash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Instance ?? string.Empty);
                int databaseHash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Database ?? string.Empty);
                return (instanceHash * 397) ^ databaseHash;
            }
        }
    }
}