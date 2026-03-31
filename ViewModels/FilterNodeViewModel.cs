using SSMS.ObjectAggregator.Models;

namespace SSMS.ObjectAggregator.ViewModels;

public class FilterNodeViewModel : DefinitionTreeNodeViewModel
{
    public FilterNodeViewModel(FilterDefinition model, InstanceNodeViewModel parent) : base(DefinitionNodeType.Filter)
    {
        Model = model;
        ParentInstance = parent;
    }

    public FilterDefinition Model { get; }

    public InstanceNodeViewModel ParentInstance { get; }

    public override string DisplayName => $"Schema: {FormatValue(Model.SchemaFilter)}  |  Object: {FormatValue(Model.ObjectFilter)}";

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "*" : value!;
    }
}