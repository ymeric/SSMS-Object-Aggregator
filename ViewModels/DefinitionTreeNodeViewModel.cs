using System.Collections.ObjectModel;

namespace SSMS.ObjectAggregator.ViewModels;

public abstract class DefinitionTreeNodeViewModel : ViewModelBase
{
    private bool _isEditing;
    private string _editableName = string.Empty;

    protected DefinitionTreeNodeViewModel(DefinitionNodeType nodeType)
    {
        NodeType = nodeType;
        Children = new ObservableCollection<DefinitionTreeNodeViewModel>();
    }

    public DefinitionNodeType NodeType { get; }

    public ObservableCollection<DefinitionTreeNodeViewModel> Children { get; }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string EditableName
    {
        get => _editableName;
        set => SetProperty(ref _editableName, value);
    }

    public abstract string DisplayName { get; }
}

public enum DefinitionNodeType
{
    Instance,
    Filter
}