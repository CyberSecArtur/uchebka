using System.Windows;
using System.Windows.Controls;

namespace StockDesk.Desktop.Views;

public partial class UsersView : UserControl
{
    public UsersView() => InitializeComponent();

    internal event EventHandler<RoutedEventArgs>? AddUserRequested;
    private void AddUser_Click(object sender, RoutedEventArgs e) => AddUserRequested?.Invoke(sender, e);
}
