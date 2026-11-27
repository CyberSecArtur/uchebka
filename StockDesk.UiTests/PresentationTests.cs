using StockDesk.Core;
using StockDesk.Desktop;
using Xunit;

namespace StockDesk.UiTests;

public class PresentationTests
{
    [Fact]
    public void FilterCombinesSearchAndStatus()
    {
        OrderRow[] source =
        [
            new(1, "Анна", "Лесная", "Товары", null, Status.New, 0, DateTime.UtcNow, null, null, 0),
            new(2, "ООО Маяк", "Лесная", "Товары", null, Status.Shipped, 1500, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, 3)
        ];
        Assert.Equal(2, Assert.Single(Presentation.Filter(source, Status.Shipped, " лесная ")).Id);
        Assert.Empty(Presentation.Filter(source, Status.New, "маяк"));
    }

    [Fact]
    public void WarehouseWorkerCannotSeePaymentOrCancellationInStatusDialog()
    {
        Assert.Empty(Presentation.Next(Status.Packed, Role.WarehouseWorker));
        Assert.Equal(Status.Packed, Assert.Single(Presentation.Next(Status.Picking, Role.WarehouseWorker)).Value);
    }

    [Fact]
    public void WindowLoadsWithLoginVisibleAndWorkspaceHidden()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)window.FindName("LoginPanel")).Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, ((System.Windows.FrameworkElement)window.FindName("Workspace")).Visibility);
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}