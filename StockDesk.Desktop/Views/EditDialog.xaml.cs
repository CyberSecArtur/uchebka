using System.Windows;
using System.Windows.Controls;

namespace StockDesk.Desktop.Views;

public partial class EditDialog : Window
{
    public EditDialog() => InitializeComponent();

    public void SetFields((string Label, Control Input)[] fields)
    {
        Fields.ItemsSource = fields.Select(f => new EditorField(f.Label, f.Input));
        Loaded += (_, _) => fields.FirstOrDefault().Input?.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    public sealed record EditorField(string Label, Control Input);
}
