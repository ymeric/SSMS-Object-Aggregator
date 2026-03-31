using SSMS.ObjectAggregator.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SSMS.ObjectAggregator.Views;

public partial class GroupDefinitionWindow : Window
{
    public GroupDefinitionWindow(GroupDefinitionEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        Title = $"Group Definition - {viewModel.Group.Name}";
        viewModel.Group.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GroupViewModel.Name))
            {
                Title = $"Group Definition - {viewModel.Group.Name}";
            }
        };
    }

    public GroupDefinitionEditorViewModel ViewModel { get; }

    private void DefinitionTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectedNode = e.NewValue as DefinitionTreeNodeViewModel;
    }

    private void InstanceEditorPanel_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Panel panel)
        {
            var firstTextBox = panel.Children.OfType<TextBox>().FirstOrDefault();
            firstTextBox?.Focus();
            firstTextBox?.SelectAll();
        }
    }

    private void InstanceEditorPanel_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is Panel panel && panel.DataContext is InstanceNodeViewModel instance && instance.IsEditing)
        {
            if (panel.IsKeyboardFocusWithin)
            {
                return;
            }

            CommitInstanceRename(instance);
        }
    }

    private void InstanceEditor_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not InstanceNodeViewModel instance)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitInstanceRename(instance);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelInstanceRename(instance);
            e.Handled = true;
        }
    }

    private void CommitInstanceRename(InstanceNodeViewModel instance)
    {
        if (!ViewModel.TryCommitInstanceRename(instance, instance.EditableName, instance.EditableDatabaseName, out string? error))
        {
            MessageBox.Show(error, "Instance Definition", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DefinitionTree_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.SelectedNode is InstanceNodeViewModel editingInstance && editingInstance.IsEditing)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Delete when ViewModel.SelectedNode is not null:
                if (ViewModel.DeleteInstanceCommand.CanExecute(null))
                {
                    ViewModel.DeleteInstanceCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case Key.Insert when ViewModel.AddInstanceCommand.CanExecute(null):
                ViewModel.AddInstanceCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F2:
                if (ViewModel.EditInstanceCommand.CanExecute(null))
                {
                    ViewModel.EditInstanceCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }
}