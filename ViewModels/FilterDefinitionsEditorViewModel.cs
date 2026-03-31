using SSMS.ObjectAggregator.Models;
using SSMS.ObjectAggregator.Views;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace SSMS.ObjectAggregator.ViewModels;

public class FilterDefinitionsEditorViewModel : ViewModelBase
{
    private readonly Action _markGroupDirty;
    private readonly Action _persistChanges;
    private readonly AsyncRelayCommand _insertCommand;
    private readonly AsyncRelayCommand<FilterDefinition> _editCommand;
    private readonly AsyncRelayCommand<FilterDefinition> _deleteCommand;
    private FilterDefinition? _selectedFilter;

    public FilterDefinitionsEditorViewModel(InstanceNodeViewModel instance, Action markGroupDirty, Action persistChanges)
    {
        Instance = instance;
        _markGroupDirty = markGroupDirty;
        _persistChanges = persistChanges;

        Filters = new ObservableCollection<FilterDefinition>(instance.Model.Filters);

        _insertCommand = new AsyncRelayCommand(InsertAsync);
        _editCommand = new AsyncRelayCommand<FilterDefinition>(EditAsync, filter => filter is not null || SelectedFilter is not null);
        _deleteCommand = new AsyncRelayCommand<FilterDefinition>(DeleteAsync, filter => filter is not null || SelectedFilter is not null);
    }

    public InstanceNodeViewModel Instance { get; }

    public ObservableCollection<FilterDefinition> Filters { get; }

    public FilterDefinition? SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                _editCommand.RaiseCanExecuteChanged();
                _deleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand InsertCommand => _insertCommand;

    public ICommand EditCommand => _editCommand;

    public ICommand DeleteCommand => _deleteCommand;

    private Task InsertAsync()
    {
        var request = new FilterEditRequest();
        if (!FilterEditRequestPrompt.Show(request))
        {
            return Task.CompletedTask;
        }

        var definition = new FilterDefinition
        {
            SchemaFilter = request.SchemaFilter,
            ObjectFilter = request.ObjectFilter
        };

        Instance.Model.Filters.Add(definition);
        Filters.Add(definition);
        SelectedFilter = definition;
        OnFiltersChanged();
        return Task.CompletedTask;
    }

    private Task EditAsync(FilterDefinition? filter)
    {
        var target = filter ?? SelectedFilter;
        if (target is null)
        {
            return Task.CompletedTask;
        }

        var request = new FilterEditRequest
        {
            SchemaFilter = target.SchemaFilter,
            ObjectFilter = target.ObjectFilter
        };

        if (!FilterEditRequestPrompt.Show(request))
        {
            return Task.CompletedTask;
        }

        target.SchemaFilter = request.SchemaFilter;
        target.ObjectFilter = request.ObjectFilter;
        int index = Filters.IndexOf(target);
        if (index >= 0)
        {
            Filters[index] = target;
        }
        OnFiltersChanged();
        return Task.CompletedTask;
    }

    private Task DeleteAsync(FilterDefinition? filter)
    {
        var target = filter ?? SelectedFilter;
        if (target is null)
        {
            return Task.CompletedTask;
        }

        var result = MessageBox.Show("Delete this filter definition?", "Delete Filter", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return Task.CompletedTask;
        }

        Instance.Model.Filters.Remove(target);
        Filters.Remove(target);
        SelectedFilter = Filters.FirstOrDefault();
        OnFiltersChanged();
        return Task.CompletedTask;
    }

    private void OnFiltersChanged()
    {
        Instance.RefreshFilters();
        _markGroupDirty();
        _persistChanges();
        _editCommand.RaiseCanExecuteChanged();
        _deleteCommand.RaiseCanExecuteChanged();
    }
}