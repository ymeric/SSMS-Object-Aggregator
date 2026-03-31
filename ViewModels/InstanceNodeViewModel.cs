using SSMS.ObjectAggregator.Models;

namespace SSMS.ObjectAggregator.ViewModels;

public class InstanceNodeViewModel : DefinitionTreeNodeViewModel
{
    private string _editableDatabaseName = string.Empty;

    public InstanceNodeViewModel(GroupCriterion model) : base(DefinitionNodeType.Instance)
    {
        Model = model;
        EditableName = model.InstanceName;
        EditableDatabaseName = model.DatabaseName;
        foreach (var filter in model.Filters)
        {
            Children.Add(new FilterNodeViewModel(filter, this));
        }
    }

    public GroupCriterion Model { get; }

    public string DatabaseName => Model.DatabaseName;

    public string EditableDatabaseName
    {
        get => _editableDatabaseName;
        set => SetProperty(ref _editableDatabaseName, value);
    }

    public override string DisplayName
    {
        get
        {
            string instance = string.IsNullOrWhiteSpace(Model.InstanceName) ? "(Unnamed Instance)" : Model.InstanceName;
            string database = string.IsNullOrWhiteSpace(Model.DatabaseName) ? "(Database?)" : Model.DatabaseName;
            return $"{instance}  —  {database}";
        }
    }

    public IEnumerable<FilterNodeViewModel> Filters => Children.Cast<FilterNodeViewModel>();

    public void RefreshFilters()
    {
        Children.Clear();
        foreach (var filter in Model.Filters)
        {
            Children.Add(new FilterNodeViewModel(filter, this));
        }
    }

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DatabaseName));
    }
}