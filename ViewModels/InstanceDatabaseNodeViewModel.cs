using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace SSMS.ObjectAggregator.ViewModels;

public class InstanceDatabaseNodeViewModel : ViewModelBase
{
    private string _quickSearchText = string.Empty;
    private string _activeFilter = string.Empty;
    private DispatcherTimer? _debounceTimer;
    private ListCollectionView? _filteredTypeGroups;

    public InstanceDatabaseNodeViewModel(GroupViewModel parent, string instanceName, string databaseName)
    {
        ParentGroup = parent;
        InstanceName = instanceName;
        DatabaseName = databaseName;
        TypeGroups = new ObservableCollection<ObjectTypeGroupViewModel>();
    }

    public GroupViewModel ParentGroup { get; }

    public string InstanceName { get; }

    public string DatabaseName { get; }

    public string DisplayName => $"{InstanceName} / {DatabaseName}";

    public ObservableCollection<ObjectTypeGroupViewModel> TypeGroups { get; }

    public string QuickSearchText
    {
        get => _quickSearchText;
        set
        {
            if (!SetProperty(ref _quickSearchText, value))
                return;

            if (_debounceTimer == null)
            {
                _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _debounceTimer.Tick += OnDebounceTimerTick;
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    public ICollectionView FilteredTypeGroups
    {
        get
        {
            if (_filteredTypeGroups == null)
            {
                _filteredTypeGroups = new ListCollectionView(TypeGroups);
                _filteredTypeGroups.Filter = obj =>
                    obj is ObjectTypeGroupViewModel g &&
                    (string.IsNullOrEmpty(_activeFilter) || g.FilteredChildren.Cast<object>().Any());
            }

            return _filteredTypeGroups;
        }
    }

    public void ResetQuickSearch()
    {
        _debounceTimer?.Stop();
        _quickSearchText = string.Empty;
        OnPropertyChanged(nameof(QuickSearchText));
        ApplyFilter(string.Empty);
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer!.Stop();
        ApplyFilter(_quickSearchText);
    }

    private void ApplyFilter(string text)
    {
        _activeFilter = text.Length >= 3 ? text : string.Empty;
        foreach (var typeGroup in TypeGroups)
            typeGroup.ApplyFilter(_activeFilter);
        _filteredTypeGroups?.Refresh();
    }
}