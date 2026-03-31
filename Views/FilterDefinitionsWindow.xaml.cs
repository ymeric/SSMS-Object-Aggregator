using SSMS.ObjectAggregator.ViewModels;
using System.Windows;

namespace SSMS.ObjectAggregator.Views;

public partial class FilterDefinitionsWindow : Window
{
    public FilterDefinitionsWindow(InstanceNodeViewModel instance, Action markGroupDirty, Action persistChanges)
    {
        InitializeComponent();
        DataContext = new FilterDefinitionsEditorViewModel(instance, markGroupDirty, persistChanges);
        Title = $"Filter Definitions - {instance.DisplayName}";
    }
}