using SSMS.ObjectAggregator.Models;
using SSMS.ObjectAggregator.Views;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace SSMS.ObjectAggregator.ViewModels;

public class GroupDefinitionEditorViewModel : ViewModelBase
{
    private readonly Func<Task> _persistAsync;
    private readonly RelayCommand _addInstanceCommand;
    private readonly RelayCommand _editInstanceCommand;
    private readonly RelayCommand _deleteInstanceCommand;
    private readonly RelayCommand _filterDefinitionsCommand;
    private readonly RelayCommand<InstanceNodeViewModel> _editInstanceContextCommand;
    private readonly RelayCommand<InstanceNodeViewModel> _deleteInstanceContextCommand;
    private readonly RelayCommand<InstanceNodeViewModel> _filterDefinitionsContextCommand;
    private readonly DefinitionActionDescriptor[] _instanceActions;
    private DefinitionTreeNodeViewModel? _selectedNode;

    public GroupDefinitionEditorViewModel(GroupViewModel group, Func<Task> persistAsync)
    {
        Group = group;
        _persistAsync = persistAsync;

        Instances = new ObservableCollection<InstanceNodeViewModel>(group.Model.Criteria
            .Select(c => new InstanceNodeViewModel(c))
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase));

        _addInstanceCommand = new RelayCommand(() => BeginAddInstanceFlowInternal());
        _editInstanceCommand = new RelayCommand(() => BeginInstanceRename(), () => SelectedNode is InstanceNodeViewModel);
        _deleteInstanceCommand = new RelayCommand(DeleteSelectedInstance, () => SelectedNode is InstanceNodeViewModel);
        _filterDefinitionsCommand = new RelayCommand(OpenFilterDefinitionsForSelection, () => GetTargetInstance() is not null);
        _editInstanceContextCommand = new RelayCommand<InstanceNodeViewModel>(EditInstanceForNode, _ => true);
        _deleteInstanceContextCommand = new RelayCommand<InstanceNodeViewModel>(DeleteInstanceForNode, _ => true);
        _filterDefinitionsContextCommand = new RelayCommand<InstanceNodeViewModel>(OpenFilterDefinitionsForNode, _ => true);

        _instanceActions = new[]
        {
            new DefinitionActionDescriptor("Insert", "", _addInstanceCommand),
            new DefinitionActionDescriptor("Edit", "\uE70F", _editInstanceCommand),
            new DefinitionActionDescriptor("Delete", "", _deleteInstanceCommand),
            new DefinitionActionDescriptor("Filter Definitions", "\uE71C", _filterDefinitionsCommand)
        };

        ActiveActions = new ObservableCollection<DefinitionActionDescriptor>();
        UpdateActions();
        SelectedNode = Instances.FirstOrDefault();
    }

    public GroupViewModel Group { get; }

    public ObservableCollection<InstanceNodeViewModel> Instances { get; }

    public ICommand AddInstanceCommand => _addInstanceCommand;
    public ICommand EditInstanceCommand => _editInstanceCommand;
    public ICommand DeleteInstanceCommand => _deleteInstanceCommand;
    public ICommand FilterDefinitionsCommand => _filterDefinitionsCommand;
    public ICommand EditInstanceContextCommand => _editInstanceContextCommand;
    public ICommand DeleteInstanceContextCommand => _deleteInstanceContextCommand;
    public ICommand FilterDefinitionsContextCommand => _filterDefinitionsContextCommand;

    public ObservableCollection<DefinitionActionDescriptor> ActiveActions { get; }

    public DefinitionTreeNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                UpdateActions();
                RaiseCommandStates();
            }
        }
    }

    public void BeginAddInstanceFlow()
    {
        BeginAddInstanceFlowInternal();
    }

    public bool TryCommitInstanceRename(InstanceNodeViewModel instance, string newName, string newDatabase, out string? errorMessage)
    {
        errorMessage = null;
        string trimmedInstance = newName?.Trim() ?? string.Empty;
        string trimmedDatabase = newDatabase?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedInstance))
        {
            errorMessage = "Instance name cannot be empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(trimmedDatabase))
        {
            errorMessage = "Database name cannot be empty.";
            return false;
        }

        bool duplicate = Group.Model.Criteria
            .Where(c => !ReferenceEquals(c, instance.Model))
            .Any(c => string.Equals(c.InstanceName, trimmedInstance, StringComparison.OrdinalIgnoreCase)
                      && string.Equals(c.DatabaseName, trimmedDatabase, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            errorMessage = "This instance/database pair already exists in the group.";
            return false;
        }

        instance.Model.InstanceName = trimmedInstance;
        instance.EditableName = trimmedInstance;
        instance.Model.DatabaseName = trimmedDatabase;
        instance.EditableDatabaseName = trimmedDatabase;
        instance.IsEditing = false;
        instance.NotifyChanged();
        Group.MarkObjectsStale();
        ResortInstances();
        PersistChanges();
        return true;
    }

    public void CancelInstanceRename(InstanceNodeViewModel instance)
    {
        if (string.IsNullOrWhiteSpace(instance.Model.InstanceName) || string.IsNullOrWhiteSpace(instance.Model.DatabaseName))
        {
            RemoveInstance(instance);
        }
        else
        {
            instance.EditableName = instance.Model.InstanceName;
            instance.EditableDatabaseName = instance.Model.DatabaseName;
            instance.IsEditing = false;
        }
    }

    private void BeginAddInstanceFlowInternal()
    {
        var criterion = new GroupCriterion();
        Group.Model.Criteria.Add(criterion);
        var node = new InstanceNodeViewModel(criterion);
        Instances.Add(node);
        SelectedNode = node;
        node.EditableName = string.Empty;
        node.EditableDatabaseName = string.Empty;
        node.IsEditing = true;
    }

    private void BeginInstanceRename()
    {
        if (SelectedNode is InstanceNodeViewModel instance)
        {
            instance.EditableName = instance.Model.InstanceName;
            instance.EditableDatabaseName = instance.Model.DatabaseName;
            instance.IsEditing = true;
        }
    }

    private void DeleteSelectedInstance()
    {
        if (SelectedNode is not InstanceNodeViewModel instance)
        {
            return;
        }

        var result = MessageBox.Show($"Are you sure you want to delete instance '{instance.DisplayName}'?", "Delete Instance", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        RemoveInstance(instance);
        PersistChanges();
    }

    private void RemoveInstance(InstanceNodeViewModel instance)
    {
        Group.Model.Criteria.Remove(instance.Model);
        Instances.Remove(instance);
        SelectedNode = Instances.FirstOrDefault();
        Group.MarkObjectsStale();
    }

    private void OpenFilterDefinitionsForSelection()
    {
        var target = GetTargetInstance();
        if (target is null)
        {
            return;
        }

        OpenFilterDefinitions(target);
    }

    private void OpenFilterDefinitionsForNode(InstanceNodeViewModel? instance)
    {
        var target = instance ?? GetTargetInstance();
        if (target is null)
        {
            return;
        }

        SelectedNode = target;
        OpenFilterDefinitions(target);
    }

    private void OpenFilterDefinitions(InstanceNodeViewModel instance)
    {
        try
        {
            var window = new FilterDefinitionsWindow(instance, Group.MarkObjectsStale, PersistChanges)
            {
                Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Filter Definitions", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private InstanceNodeViewModel? GetTargetInstance()
    {
        return SelectedNode switch
        {
            InstanceNodeViewModel instance => instance,
            FilterNodeViewModel filter => filter.ParentInstance,
            _ => null
        };
    }

    private void UpdateActions()
    {
        ActiveActions.Clear();
        foreach (var descriptor in _instanceActions)
        {
            ActiveActions.Add(descriptor);
        }
    }

    private void RaiseCommandStates()
    {
        _editInstanceCommand.RaiseCanExecuteChanged();
        _deleteInstanceCommand.RaiseCanExecuteChanged();
        _filterDefinitionsCommand.RaiseCanExecuteChanged();
        _editInstanceContextCommand.RaiseCanExecuteChanged();
        _deleteInstanceContextCommand.RaiseCanExecuteChanged();
        _filterDefinitionsContextCommand.RaiseCanExecuteChanged();
    }

    private void ResortInstances()
    {
        var ordered = Instances.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (!ReferenceEquals(Instances[i], ordered[i]))
            {
                Instances.Move(Instances.IndexOf(ordered[i]), i);
            }
        }
    }

    private void EditInstanceForNode(InstanceNodeViewModel? instance)
    {
        var target = instance ?? GetTargetInstance();
        if (target is null)
        {
            return;
        }

        SelectedNode = target;
        BeginInstanceRename();
    }

    private void DeleteInstanceForNode(InstanceNodeViewModel? instance)
    {
        var target = instance ?? GetTargetInstance();
        if (target is null)
        {
            return;
        }

        SelectedNode = target;
        DeleteSelectedInstance();
    }

    private void PersistChanges()
    {
        try
        {
#pragma warning disable VSTHRD002 // Sync-over-async: called from sync RelayCommand; storage I/O does not marshal to UI thread
            _persistAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Group", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}