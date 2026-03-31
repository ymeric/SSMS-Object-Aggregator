using SSMS.ObjectAggregator.ViewModels;
using System.Windows;

namespace SSMS.ObjectAggregator.Views;

internal static class FilterEditRequestPrompt
{
    public static bool Show(FilterEditRequest request)
    {
        var dialog = new FilterDefinitionDialog();
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.SetValues(request.SchemaFilter, request.ObjectFilter);

        if (dialog.ShowDialog() == true)
        {
            request.SchemaFilter = dialog.SchemaFilter;
            request.ObjectFilter = dialog.ObjectFilter;
            return true;
        }

        return false;
    }
}