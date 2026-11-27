using StockDesk.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace StockDesk.Desktop.Views;

public partial class StatisticsView : UserControl
{
    public StatisticsView() => InitializeComponent();

    internal event EventHandler<RoutedEventArgs>? ExportRequested;
    private void Export_Click(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(sender, e);
    internal event EventHandler<RoutedEventArgs>? RefreshRequested;
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public void ShowReport(Statistics report, bool assignedOnly = false)
    {
        CustomerMetric.Text = report.Customers.ToString(Russian);
        TotalMetric.Text = report.Total.ToString(Russian);
        ActiveMetric.Text = report.Active.ToString(Russian);
        RevenueMetric.Text = Currency(report.Revenue);
        StatusChart.ItemsSource = report.ByStatus.Select(pair => $"{pair.Key}  {pair.Value}").ToArray();
        Duration.Text = $"Средний срок · {report.AverageHours.ToString("N1", Russian)} ч"
            + (assignedOnly ? "  ·  Только назначенные записи" : "");
        var months = report.Months ?? [];
        decimal max = months.Select(m => m.Amount).DefaultIfEmpty(0).Max();
        RevenueChart.ItemsSource = months.Select(m => new RevenueBar(
            m.Month.ToString("MMM yy", Russian), Currency(m.Amount),
            max <= 0 ? 2 : Math.Max(2, (double)(m.Amount / max) * 82))).ToArray();
    }

    private static string Currency(decimal value) => value.ToString("N0", Russian) + " ₽";
    public sealed record RevenueBar(string Label, string Amount, double Height);
}
