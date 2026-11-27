using System.Windows;
using System.Windows.Controls;

namespace StockDesk.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    internal event EventHandler<RoutedEventArgs>? LoginRequested;
    private void Login_Click(object sender, RoutedEventArgs e) => LoginRequested?.Invoke(sender, e);
}
