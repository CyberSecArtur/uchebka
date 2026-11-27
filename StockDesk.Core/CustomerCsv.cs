using Microsoft.VisualBasic.FileIO;

namespace StockDesk.Core;

public static class CustomerCsv
{
    public static (List<(int Line, CustomerInput Input)> Customers, List<ImportIssue> Issues) Parse(string source)
    {
        List<(int, CustomerInput)> customers = [];
        List<ImportIssue> issues = [];
        using var reader = new StringReader(source.TrimStart('\uFEFF'));
        if (!string.Equals(reader.ReadLine()?.Trim(), "name;address", StringComparison.OrdinalIgnoreCase))
            return (customers, [new ImportIssue(1, "Ожидается заголовок name;address.")]);

        int line = 1;
        while (reader.ReadLine() is { } text)
        {
            line++;
            if (string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                using var parser = new TextFieldParser(new StringReader(text));
                parser.SetDelimiters(";");
                var fields = parser.ReadFields();
                if (fields is not [var name, var address] || name.Length is < 1 or > 120 ||
                    address.Length is < 1 or > 250 || name.Any(char.IsControl) || address.Any(char.IsControl))
                    issues.Add(new ImportIssue(line, "Укажите имя и адрес в двух столбцах."));
                else
                    customers.Add((line, new CustomerInput(name, address)));
            }
            catch (MalformedLineException)
            {
                issues.Add(new ImportIssue(line, "Некорректные кавычки CSV."));
            }
        }
        return (customers, issues);
    }

    public static string Cell(string value)
    {
        if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}