using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using StockDesk.Desktop.Views;
using StockDesk.Core;
using Microsoft.Win32;

namespace StockDesk.Desktop;

public partial class MainWindow : Window
{
    private readonly HttpClient http = new()
    {
        BaseAddress = App.ApiAddress,
        Timeout = TimeSpan.FromSeconds(12)
    };
    private List<OrderRow> orders = [];
    private List<CustomerRow> customers = [];
    private Role role;

    private TextBox Login => LoginPanel.Login;
    private PasswordBox Password => LoginPanel.Password;
    private Button AddOrder => OrderPage.AddOrder;
    private Button ChangeOrder => OrderPage.ChangeOrder;
    private ListBox Filter => OrderPage.Filter;
    private TextBox Search => OrderPage.Search;
    private DataGrid Orders => OrderPage.Orders;
    private DataGrid Customers => CustomerPage.Customers;
    private WrapPanel ExportButtons => StatsPage.ExportButtons;
    private DataGrid Users => UserPage.Users;

    public MainWindow()
    {
        InitializeComponent();
        LoginPanel.LoginRequested += Login_Click;
        OrderPage.AddOrderRequested += AddOrder_Click;
        OrderPage.ChangeOrderRequested += ChangeOrder_Click;
        OrderPage.FilterChanged += Filter_Changed;
        OrderPage.RefreshRequested += Refresh_Click;
        OrderPage.SearchChanged += Search_Changed;
        CustomerPage.AddCustomerRequested += AddCustomer_Click;
        CustomerPage.ImportRequested += Import_Click;
        StatsPage.ExportRequested += Export_Click;
        StatsPage.RefreshRequested += Refresh_Click;
        UserPage.AddUserRequested += AddUser_Click;
        Filter.ItemsSource = new[] { new Choice<Status?>(null, "Все статусы") }.Concat(
            Enum.GetValues<Status>().Select(s => new Choice<Status?>(s, Labels.StatusName(s))));
        Filter.SelectedIndex = 0;
        Closed += (_, _) => http.Dispose();
    }

    private async void Login_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        using var response = await Send(HttpMethod.Post, "/api/login", JsonContent.Create(new LoginRequest(Login.Text, Password.Password)));
        var reply = await Read<LoginReply>(response);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", reply.Token);
        role = reply.Role;
        await Refresh();
        Identity.Text = $"{Login.Text.Trim()} · {Labels.RoleName(role)}";
        Password.Clear();
        CustomerTab.Visibility = role == Role.WarehouseWorker ? Visibility.Collapsed : Visibility.Visible;
        UserTab.Visibility = ExportButtons.Visibility = role == Role.Administrator ? Visibility.Visible : Visibility.Collapsed;
        AddOrder.Visibility = role == Role.WarehouseWorker ? Visibility.Collapsed : Visibility.Visible;
        LoginPanel.Visibility = Visibility.Collapsed;
        Workspace.Visibility = SessionPanel.Visibility = Visibility.Visible;
        Workspace.SelectedIndex = 0;
    });

    private void Logout_Click(object? sender, RoutedEventArgs e) => SignOutNow();

    private void SignOutNow()
    {
        http.DefaultRequestHeaders.Authorization = null;
        orders = [];
        customers = [];
        Orders.ItemsSource = Customers.ItemsSource = Users.ItemsSource = null;
        Password.Clear();
        Workspace.Visibility = SessionPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
        Identity.Text = "";
        Search.Clear();
        Filter.SelectedIndex = 0;
        Message.Text = "Введите логин и пароль.";
        Login.Focus();
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await Run(Refresh);

    private async Task Refresh()
    {
        var timer = Stopwatch.StartNew();
        orders = await Get<List<OrderRow>>("/api/orders");
        ApplyFilter();
        if (role != Role.WarehouseWorker)
        {
            customers = await Get<List<CustomerRow>>("/api/customers");
            Customers.ItemsSource = customers;
        }
        if (role == Role.Administrator)
        {
            var users = await Get<List<UserRow>>("/api/users");
            Users.ItemsSource = users.Select(u => new { u.Login, Role = Labels.RoleName(u.Role) });
        }
        var s = await Get<Statistics>("/api/statistics");
        StatsPage.ShowReport(s, role == Role.WarehouseWorker);
        Trace.WriteLine($"Обновление данных: {timer.ElapsedMilliseconds} мс.");
    }

    private void Filter_Changed(object? sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void Search_Changed(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (Orders is null || Search is null || Filter is null) return;
        var selected = (Orders.SelectedItem as OrderRow)?.Id;
        var result = Presentation.Filter(orders, (Filter.SelectedItem as Choice<Status?>)?.Value, Search.Text).ToList();
        Orders.ItemsSource = result;
        Orders.SelectedItem = result.FirstOrDefault(o => o.Id == selected);
    }

    private async void AddOrder_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (customers.Count == 0) throw new RuleException("Сначала добавьте клиента и адрес.");
        var customer = ListBoxFor(customers);
        var description = new TextBox { MaxLength = 500 };
        if (!Edit("Новая заявка", ("Покупатель и адрес", customer), ("Товары и количество", description))) return;
        await Mutate(HttpMethod.Post, "/api/orders", new NewOrder(((CustomerRow)customer.SelectedItem).Id, description.Text));
    });

    private async void ChangeOrder_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Orders.SelectedItem is not OrderRow row) throw new RuleException("Выберите заказ в списке.");
        var options = Presentation.Next(row.Status, role);
        if (options.Count == 0) throw new RuleException("Для этого заказа нет доступных переходов.");
        var status = ListBoxFor(options);
        var workers = options.Any(o => o.Value == Status.Picking)
            ? await Get<List<UserRow>>("/api/workers") : [];
        var worker = ListBoxFor(workers);
        var price = new TextBox { MaxLength = 12 };
        var fields = new List<(string, Control)> { ("Новый статус", status) };
        if (options.Any(o => o.Value == Status.Picking)) fields.Add(("Кладовщик для комплектации", worker));
        if (options.Any(o => o.Value == Status.Packed)) fields.Add(("Сумма заявки, ₽", price));
        if (!Edit($"Заявка № {row.Id}", fields.ToArray())) return;
        var next = ((Choice<Status>)status.SelectedItem).Value;
        decimal? amount = null;
        if (next == Status.Packed)
        {
            if (!decimal.TryParse(price.Text.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal parsed)) throw new RuleException("Введите сумму числом.");
            amount = parsed;
        }
        await Mutate(HttpMethod.Patch, $"/api/orders/{row.Id}",
            new OrderChange(next, (worker.SelectedItem as UserRow)?.Id, amount, row.Revision));
    });

    private async void AddCustomer_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var name = new TextBox { MaxLength = 120 };
        var address = new TextBox { MaxLength = 250 };
        if (Edit("Новый покупатель", ("Имя или организация", name), ("Адрес поставки", address)))
            await Mutate(HttpMethod.Post, "/api/customers", new CustomerInput(name.Text, address.Text));
    });

    private async void AddUser_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var login = new TextBox { MaxLength = 60 };
        var password = new PasswordBox { MaxLength = 128 };
        var selected = ListBoxFor(Enum.GetValues<Role>().Select(r => new Choice<Role>(r, Labels.RoleName(r))));
        selected.SelectedIndex = 2;
        if (Edit("Новый пользователь", ("Логин", login), ("Пароль, от 10 символов", password), ("Роль", selected)))
            await Mutate(HttpMethod.Post, "/api/users", new UserInput(login.Text, password.Password, ((Choice<Role>)selected.SelectedItem).Value));
    });

    private async void Import_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv" };
        if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > 262144) throw new RuleException("Файл должен быть меньше 256 КБ.");
        using var response = await Send(HttpMethod.Post, "/api/customers/import", new StreamContent(File.OpenRead(dialog.FileName)));
        var result = await Read<ImportReply>(response);
        await Refresh();
        var summary = $"Добавлено: {result.Added}. Ошибок: {result.Issues.Count}.";
        if (result.Issues.Count > 0)
            MessageBox.Show(this, summary + "\n" + string.Join("\n", result.Issues.Take(15).Select(x => $"Строка {x.Line}: {x.Message}")),
                "Результат импорта");
    });

    private async void Export_Click(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (sender is not Button { Tag: string format }) return;
        var dialog = new SaveFileDialog { Filter = $"{format.ToUpperInvariant()} (*.{format})|*.{format}", FileName = $"orders.{format}" };
        if (dialog.ShowDialog(this) != true) return;
        using var response = await Send(HttpMethod.Get, $"/api/export/{format}");
        await File.WriteAllBytesAsync(dialog.FileName, await response.Content.ReadAsByteArrayAsync());
    });

    private static ComboBox ListBoxFor(System.Collections.IEnumerable items) => new() { ItemsSource = items, SelectedIndex = 0 };

    private bool Edit(string title, params (string Label, Control Input)[] fields)
    {
        var dialog = new EditDialog { Title = title, Owner = this };
        dialog.SetFields(fields);
        return dialog.ShowDialog() == true;
    }

    private async Task<T> Get<T>(string path)
    {
        using var response = await Send(HttpMethod.Get, path);
        return await Read<T>(response);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>() ?? throw new RuleException("Сервер вернул пустой ответ.");

    private async Task Mutate<T>(HttpMethod method, string path, T body)
    {
        using var response = await Send(method, path, JsonContent.Create(body));
        await Refresh();
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && path != "/api/login")
            {
                SignOutNow();
                throw new RuleException("Сеанс завершён. Войдите снова.");
            }
            var error = response.Content.Headers.ContentType?.MediaType == "application/json"
                ? await response.Content.ReadFromJsonAsync<ApiError>() : null;
            throw new RuleException(error?.Message ?? $"Ошибка сервера: {(int)response.StatusCode}.");
        }
    }

    private async Task Run(Func<Task> action)
    {
        IsEnabled = false;
        try { Message.Text = ""; await action(); }
        catch (RuleException ex) { Message.Text = ex.Message; }
        catch (HttpRequestException ex) { Trace.TraceError(ex.Message); Message.Text = "Нет связи с API. Проверьте, что сервер запущен."; }
        catch (TaskCanceledException) { Message.Text = "Сервер не ответил вовремя."; }
        catch (Exception ex) { Trace.TraceError(ex.ToString()); Message.Text = "Не удалось выполнить операцию. " + ex.Message; }
        finally { IsEnabled = true; }
    }
}
