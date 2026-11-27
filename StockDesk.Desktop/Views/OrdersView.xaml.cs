using System.Windows;
using System.Windows.Controls;

namespace StockDesk.Desktop.Views;

public partial class OrdersView : UserControl
{
    public OrdersView() => InitializeComponent();

    internal event EventHandler<RoutedEventArgs>? AddOrderRequested;
    private void AddOrder_Click(object sender, RoutedEventArgs e) => AddOrderRequested?.Invoke(sender, e);
    internal event EventHandler<RoutedEventArgs>? ChangeOrderRequested;
    private void ChangeOrder_Click(object sender, RoutedEventArgs e) => ChangeOrderRequested?.Invoke(sender, e);
    internal event EventHandler<SelectionChangedEventArgs>? FilterChanged;
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => FilterChanged?.Invoke(sender, e);
    internal event EventHandler<RoutedEventArgs>? RefreshRequested;
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
    internal event EventHandler<TextChangedEventArgs>? SearchChanged;
    private void Search_Changed(object sender, TextChangedEventArgs e) => SearchChanged?.Invoke(sender, e);
}
