namespace MizhbankNotifier.Models;

public record InterbankRate(DateTime Time, decimal Buy, decimal Sell, long TimestampMs = 0);
