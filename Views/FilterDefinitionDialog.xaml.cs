using System.Windows;
using System.Windows.Controls;

namespace SSMS.ObjectAggregator.Views;

public partial class FilterDefinitionDialog : Window
{
    public FilterDefinitionDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SchemaTextBox.Focus();
            SchemaTextBox.SelectAll();
            UpdateState();
        };
    }

    public string? SchemaFilter => string.IsNullOrWhiteSpace(SchemaTextBox.Text) ? null : SchemaTextBox.Text.Trim();

    public string? ObjectFilter => string.IsNullOrWhiteSpace(ObjectTextBox.Text) ? null : ObjectTextBox.Text.Trim();

    public void SetValues(string? schemaFilter, string? objectFilter)
    {
        SchemaTextBox.Text = schemaFilter ?? string.Empty;
        ObjectTextBox.Text = objectFilter ?? string.Empty;
        SchemaTextBox.CaretIndex = SchemaTextBox.Text.Length;
        ObjectTextBox.CaretIndex = ObjectTextBox.Text.Length;
        UpdateState();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateState();
    }

    private void UpdateState()
    {
        bool isValid = !string.IsNullOrWhiteSpace(SchemaTextBox.Text) || !string.IsNullOrWhiteSpace(ObjectTextBox.Text);
        OkButton.IsEnabled = isValid;
        WarningText.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!OkButton.IsEnabled)
        {
            return;
        }

        DialogResult = true;
    }
}