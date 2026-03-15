namespace MizhbankNotifier.Models;

public record BankRate(string Name, decimal Buy, decimal Sell, string UpdatedAt, bool IsOptimal = false);
