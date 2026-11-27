using System.Windows;
using System.Windows.Controls;

namespace StockDesk.Desktop.Views;

public partial class CustomersView : UserControl
{
    public CustomersView() => InitializeComponent();

    internal event EventHandler<RoutedEventArgs>? AddCustomerRequested;
    private void AddCustomer_Click(object sender, RoutedEventArgs e) => AddCustomerRequested?.Invoke(sender, e);
    internal event EventHandler<RoutedEventArgs>? ImportRequested;
    private void Import_Click(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(sender, e);
}
