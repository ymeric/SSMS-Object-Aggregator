using SSMS.ObjectAggregator.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace SSMS.ObjectAggregator.ViewModels;

public class ObjectTypeGroupViewModel
{
    private ListCollectionView? _filteredChildren;

    public ObjectTypeGroupViewModel(InstanceDatabaseNodeViewModel parent, string objectTypeKey)
    {
        Parent = parent;
        ObjectTypeKey = objectTypeKey;
        DisplayName = ObjectTypeDisplayHelper.GetDisplayName(objectTypeKey);
        SortOrder = ObjectTypeDisplayHelper.GetOrder(objectTypeKey);
        Children = new ObservableCollection<SqlObjectNodeViewModel>();
    }

    public InstanceDatabaseNodeViewModel Parent { get; }

    public GroupViewModel ParentGroup => Parent.ParentGroup;

    public string ObjectTypeKey { get; }

    public string DisplayName { get; }

    public int SortOrder { get; }

    public ObservableCollection<SqlObjectNodeViewModel> Children { get; }

    public ICollectionView FilteredChildren
    {
        get
        {
            if (_filteredChildren == null)
                _filteredChildren = new ListCollectionView(Children);
            return _filteredChildren;
        }
    }

    public void ApplyFilter(string? searchText)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            FilteredChildren.Filter = null;
        }
        else
        {
            FilteredChildren.Filter = obj =>
                obj is SqlObjectNodeViewModel vm &&
                vm.DisplayName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}