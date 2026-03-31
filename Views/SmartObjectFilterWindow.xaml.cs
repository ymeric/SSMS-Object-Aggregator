#pragma warning disable VSTHRD100 // async void is required for WPF event handlers
using SSMS.ObjectAggregator.Infrastructure;
using SSMS.ObjectAggregator.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SSMS.ObjectAggregator.Views;

public partial class SmartObjectFilterWindow : UserControl
{
    private DateTime _lastF2Press = DateTime.MinValue;

    public SmartObjectFilterWindow()
        : this(AggregatorServiceProvider.Services)
    {
    }

    internal SmartObjectFilterWindow(IAggregatorServices services)
    {
        InitializeComponent();
        ViewModel = new SmartObjectFilterViewModel(services.StorageService, services.ReloadService);
        DataContext = ViewModel;
        ViewModel.GroupDefinitionRequested += OnGroupDefinitionRequested;
        Loaded += SmartObjectFilterWindow_Loaded;
    }

    public SmartObjectFilterViewModel ViewModel { get; }

    private async void SmartObjectFilterWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SmartObjectFilterWindow_Loaded;
        await ViewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void GroupsTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectedObjectNode = null;

        switch (e.NewValue)
        {
            case GroupViewModel group:
                ViewModel.SelectedGroup = group;
                break;

            case InstanceDatabaseNodeViewModel instanceNode:
                ViewModel.SelectedGroup = instanceNode.ParentGroup;
                break;

            case ObjectTypeGroupViewModel typeGroup:
                ViewModel.SelectedGroup = typeGroup.ParentGroup;
                break;

            case SqlObjectNodeViewModel objectNode:
                ViewModel.SelectedGroup = objectNode.ParentGroup;
                ViewModel.SelectedObjectNode = objectNode;
                break;

            case PlaceholderNodeViewModel placeholder:
                ViewModel.SelectedGroup = placeholder.ParentGroup;
                break;
        }
    }

    private async void GroupTreeViewItem_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: GroupViewModel group })
        {
            await ViewModel.EnsureObjectsLoadedAsync(group).ConfigureAwait(true);
        }
    }

    private void GroupsTree_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (ViewModel.SelectedGroup is null)
        {
            return;
        }

        if (ViewModel.SelectedGroup.IsEditing)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F2:
                var now = DateTime.UtcNow;
                if ((now - _lastF2Press).TotalMilliseconds < 600)
                {
                    if (ViewModel.EditGroupCommand.CanExecute(null))
                    {
                        ViewModel.EditGroupCommand.Execute(null);
                    }
                }
                else
                {
                    ViewModel.BeginRenameSelectedGroup();
                }

                _lastF2Press = now;
                e.Handled = true;
                break;

            case Key.Delete when ViewModel.DeleteGroupCommand.CanExecute(null):
                ViewModel.DeleteGroupCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Insert when ViewModel.AddInstanceCommand.CanExecute(null):
                ViewModel.AddInstanceCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter when ViewModel.EditGroupCommand.CanExecute(null):
                ViewModel.EditGroupCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void GroupNameEditor_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private async void GroupNameEditor_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is GroupViewModel group && group.IsEditing)
        {
            await CommitGroupRenameAsync(group, textBox).ConfigureAwait(true);
        }
    }

    private async void GroupNameEditor_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not GroupViewModel group)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            await CommitGroupRenameAsync(group, textBox).ConfigureAwait(true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            group.CancelRename();
            e.Handled = true;
        }
    }

    private async Task CommitGroupRenameAsync(GroupViewModel group, TextBox editor)
    {
        if (!ViewModel.TryCommitGroupRename(group, editor.Text ?? string.Empty, out string? error))
        {
            MessageBox.Show(error, "Rename Group", MessageBoxButton.OK, MessageBoxImage.Warning);
            editor.Focus();
            editor.SelectAll();
            return;
        }

        await ViewModel.PersistAsync().ConfigureAwait(true);
    }

    private void QuickSearchBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape &&
            sender is TextBox textBox &&
            textBox.DataContext is InstanceDatabaseNodeViewModel vm)
        {
            vm.ResetQuickSearch();
            e.Handled = true;
        }
    }

    private void RenameGroupMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is GroupViewModel group)
        {
            ViewModel.SelectedGroup = group;
            group.BeginRename();
        }
    }

    private void EditGroupMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is GroupViewModel group)
        {
            ViewModel.SelectedGroup = group;
            if (ViewModel.EditGroupCommand.CanExecute(null))
            {
                ViewModel.EditGroupCommand.Execute(null);
            }
        }
    }

    private void DeleteGroupMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is GroupViewModel group)
        {
            ViewModel.SelectedGroup = group;
            if (ViewModel.DeleteGroupCommand.CanExecute(null))
            {
                ViewModel.DeleteGroupCommand.Execute(null);
            }
        }
    }

    private void AddInstanceMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is GroupViewModel group)
        {
            ViewModel.SelectedGroup = group;
            if (ViewModel.AddInstanceCommand.CanExecute(null))
            {
                ViewModel.AddInstanceCommand.Execute(null);
            }
        }
    }

    private void GroupTreeViewItem_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GroupsTree.SelectedItem is SqlObjectNodeViewModel selectedObject && ViewModel.LocateObjectCommand.CanExecute(selectedObject))
        {
            ViewModel.LocateObjectCommand.Execute(selectedObject);
            e.Handled = true;
            return;
        }

        if (sender is not TreeViewItem item)
        {
            return;
        }

        if (item.DataContext is GroupViewModel && ViewModel.EditGroupCommand.CanExecute(null))
        {
            ViewModel.EditGroupCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGroupDefinitionRequested(object? sender, GroupDefinitionRequestEventArgs e)
    {
        var editorViewModel = new GroupDefinitionEditorViewModel(e.Group, ViewModel.PersistAsync);
        var window = new GroupDefinitionWindow(editorViewModel)
        {
            Owner = Window.GetWindow(this)
        };

        if (e.Mode == GroupDefinitionLaunchMode.AddInstance)
        {
            RoutedEventHandler? handler = null;
            handler = (_, _) =>
            {
                window.Loaded -= handler;
                editorViewModel.BeginAddInstanceFlow();
            };
            window.Loaded += handler;
        }

        window.ShowDialog();
    }
}