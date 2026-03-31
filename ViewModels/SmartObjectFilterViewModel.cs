using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio.Shell;
using SSMS.ObjectAggregator.Infrastructure;
using SSMS.ObjectAggregator.Models;
using SSMS.ObjectAggregator.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace SSMS.ObjectAggregator.ViewModels;

public class SmartObjectFilterViewModel : ViewModelBase
{
    private readonly GroupStorageService _storageService;
    private readonly GroupReloadService _reloadService;
    private readonly AsyncRelayCommand _addGroupCommand;
    private readonly AsyncRelayCommand _deleteGroupCommand;
    private readonly AsyncRelayCommand _reloadGroupCommand;
    private readonly RelayCommand _editGroupCommand;
    private readonly RelayCommand _addInstanceCommand;
    private readonly RelayCommand<GroupViewModel> _renameGroupContextCommand;
    private readonly RelayCommand<GroupViewModel> _editGroupContextCommand;
    private readonly RelayCommand<GroupViewModel> _addInstanceContextCommand;
    private readonly AsyncRelayCommand<GroupViewModel> _deleteGroupContextCommand;
    private readonly AsyncRelayCommand<SqlObjectNodeViewModel> _locateObjectCommand;
    private SqlObjectNodeViewModel? _selectedObjectNode;
    private GroupViewModel? _selectedGroup;
    private bool _isInitialized;

    public SmartObjectFilterViewModel(GroupStorageService storageService, GroupReloadService reloadService)
    {
        _storageService = storageService;
        _reloadService = reloadService;

        Groups = new ObservableCollection<GroupViewModel>();

        _addGroupCommand = new AsyncRelayCommand(AddGroupAsync);
        _deleteGroupCommand = new AsyncRelayCommand(DeleteSelectedGroupAsync, () => SelectedGroup is not null);
        _reloadGroupCommand = new AsyncRelayCommand(ReloadSelectedGroupAsync, () => SelectedGroup is not null && !(SelectedGroup?.IsReloading ?? false));
        _editGroupCommand = new RelayCommand(OpenGroupDefinition, () => SelectedGroup is not null);
        _addInstanceCommand = new RelayCommand(OpenGroupDefinitionForInstance, () => SelectedGroup is not null);
        _renameGroupContextCommand = new RelayCommand<GroupViewModel>(BeginRenameForGroup, _ => true);
        _editGroupContextCommand = new RelayCommand<GroupViewModel>(OpenGroupDefinitionForGroup, _ => true);
        _addInstanceContextCommand = new RelayCommand<GroupViewModel>(OpenGroupDefinitionForInstanceGroup, _ => true);
        _deleteGroupContextCommand = new AsyncRelayCommand<GroupViewModel>(DeleteGroupAsync, _ => true);
        _locateObjectCommand = new AsyncRelayCommand<SqlObjectNodeViewModel>(LocateObjectAsync, _ => true);
    }

    public ObservableCollection<GroupViewModel> Groups { get; }

    public GroupViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public ICommand AddGroupCommand => _addGroupCommand;
    public ICommand DeleteGroupCommand => _deleteGroupCommand;
    public ICommand ReloadGroupCommand => _reloadGroupCommand;
    public ICommand EditGroupCommand => _editGroupCommand;
    public ICommand AddInstanceCommand => _addInstanceCommand;
    public ICommand RenameGroupContextCommand => _renameGroupContextCommand;
    public ICommand EditGroupContextCommand => _editGroupContextCommand;
    public ICommand AddInstanceContextCommand => _addInstanceContextCommand;
    public ICommand DeleteGroupContextCommand => _deleteGroupContextCommand;
    public ICommand LocateObjectCommand => _locateObjectCommand;

    public SqlObjectNodeViewModel? SelectedObjectNode
    {
        get => _selectedObjectNode;
        set => _selectedObjectNode = value;
    }

    public event EventHandler<GroupDefinitionRequestEventArgs>? GroupDefinitionRequested;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        var definitions = await _storageService.LoadAsync().ConfigureAwait(true);
        foreach (var definition in definitions.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(new GroupViewModel(definition));
        }

        _isInitialized = true;
    }

    public void BeginRenameSelectedGroup()
    {
        SelectedGroup?.BeginRename();
    }

    public bool TryCommitGroupRename(GroupViewModel group, string proposedName, out string? errorMessage)
    {
        errorMessage = null;
        string trimmed = proposedName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            errorMessage = "Group name cannot be empty.";
            return false;
        }

        if (!string.Equals(group.Name, trimmed, StringComparison.OrdinalIgnoreCase) &&
            Groups.Any(g => string.Equals(g.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = "A group with the same name already exists.";
            return false;
        }

        group.CommitRename(trimmed);
        SortGroups();
        return true;
    }

    public Task PersistAsync() => _storageService.SaveAsync(Groups.Select(g => g.Model));

    public async Task ReloadGroupAsync(GroupViewModel group)
    {
        if (group.IsReloading)
        {
            return;
        }

        group.IsReloading = true;
        group.ShowLoadingState();
        RaiseCommandStates();

        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var objects = await _reloadService.ReloadAsync(group.Model, cts.Token).ConfigureAwait(true);
            group.SetObjects(objects);
        }
        catch (Exception ex)
        {
            group.StatusMessage = ex.Message;
            MessageBox.Show($"Failed to reload '{group.Name}'.\r\n{ex.Message}", "Reload Group", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            group.IsReloading = false;
            RaiseCommandStates();
        }
    }

    public void MarkGroupAsDirty(GroupViewModel group)
    {
        group.MarkObjectsStale();
    }

    public async Task EnsureObjectsLoadedAsync(GroupViewModel group)
    {
        if (!group.HasLoadedObjects)
        {
            await ReloadGroupAsync(group).ConfigureAwait(true);
        }
    }

    private async Task AddGroupAsync()
    {
        string name = BuildUniqueGroupName("New Group");
        var model = new GroupDefinition { Name = name };
        var vm = new GroupViewModel(model);
        Groups.Add(vm);
        SelectedGroup = vm;
        vm.BeginRename();
        await PersistAsync().ConfigureAwait(true);
    }

    private async Task DeleteSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        await DeleteGroupAsync(SelectedGroup).ConfigureAwait(true);
    }

    private async Task DeleteGroupAsync(GroupViewModel? group)
    {
        var targetGroup = group ?? SelectedGroup;
        if (targetGroup is null)
        {
            return;
        }

        SelectedGroup = targetGroup;
        var result = MessageBox.Show($"Are you sure you want to delete '{targetGroup.Name}'?", "Delete Group", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        int index = Groups.IndexOf(targetGroup);
        Groups.Remove(targetGroup);
        SelectedGroup = Groups.Count == 0 ? null : Groups[Math.Min(Math.Max(index - 1, 0), Groups.Count - 1)];
        await PersistAsync().ConfigureAwait(true);
    }

    private async Task ReloadSelectedGroupAsync()
    {
        if (SelectedGroup is not null)
        {
            await ReloadGroupAsync(SelectedGroup).ConfigureAwait(true);
        }
    }

    private void OpenGroupDefinition()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        GroupDefinitionRequested?.Invoke(this, new GroupDefinitionRequestEventArgs(SelectedGroup, GroupDefinitionLaunchMode.Default));
    }

    private void OpenGroupDefinitionForGroup(GroupViewModel? group)
    {
        var targetGroup = group ?? SelectedGroup;
        if (targetGroup is null)
        {
            return;
        }

        SelectedGroup = targetGroup;
        GroupDefinitionRequested?.Invoke(this, new GroupDefinitionRequestEventArgs(targetGroup, GroupDefinitionLaunchMode.Default));
    }

    private void OpenGroupDefinitionForInstance()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        GroupDefinitionRequested?.Invoke(this, new GroupDefinitionRequestEventArgs(SelectedGroup, GroupDefinitionLaunchMode.AddInstance));
    }

    private void OpenGroupDefinitionForInstanceGroup(GroupViewModel? group)
    {
        var targetGroup = group ?? SelectedGroup;
        if (targetGroup is null)
        {
            return;
        }

        SelectedGroup = targetGroup;
        GroupDefinitionRequested?.Invoke(this, new GroupDefinitionRequestEventArgs(targetGroup, GroupDefinitionLaunchMode.AddInstance));
    }

    private void BeginRenameForGroup(GroupViewModel? group)
    {
        var targetGroup = group ?? SelectedGroup;
        if (targetGroup is null)
        {
            return;
        }

        SelectedGroup = targetGroup;
        targetGroup.BeginRename();
    }

    private async Task LocateObjectAsync(SqlObjectNodeViewModel? node)
    {
        var targetNode = node ?? SelectedObjectNode;
        if (targetNode is null)
        {
            return;
        }

        IObjectExplorerService? oeService = ServiceProvider.GlobalProvider.GetService(typeof(IObjectExplorerService)) as IObjectExplorerService;

        var (success, error) = await ObjectExplorerLocator.TryLocateAsync(targetNode.Reference, oeService);
        if (!success)
        {
            string fullMessage = error ?? "Unable to locate object in Object Explorer.";
            try { Clipboard.SetText(fullMessage); } catch { }
            MessageBox.Show(fullMessage + "\r\n\r\n(Full message copied to clipboard.)", "Locate in Object Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SortGroups()
    {
        var ordered = Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (!ReferenceEquals(Groups[i], ordered[i]))
            {
                Groups.Move(Groups.IndexOf(ordered[i]), i);
            }
        }
    }

    private string BuildUniqueGroupName(string baseName)
    {
        string name = baseName;
        int counter = 1;
        while (Groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} {counter++}";
        }

        return name;
    }

    private void RaiseCommandStates()
    {
        _deleteGroupCommand.RaiseCanExecuteChanged();
        _reloadGroupCommand.RaiseCanExecuteChanged();
        _editGroupCommand.RaiseCanExecuteChanged();
        _addInstanceCommand.RaiseCanExecuteChanged();
    }
}