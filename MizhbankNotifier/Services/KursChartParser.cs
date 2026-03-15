using System.Text.Json;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

/// <summary>
/// Parses the kurs.com.ua /ajax/getChart JSON response into a <see cref="ChartData"/>
/// that keeps the two trading sessions separate.
///
/// API series layout:
///   [0] current session Продаж  (today; may be null/sparse on weekends)
///   [1] current session Купівля
///   [2] previous session Продаж (shown grey on the website)
///   [3] previous session Купівля
/// </summary>
internal static class KursChartParser
{
    public static ChartData? Parse(string json, ILogger logger)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var viewJson = doc.RootElement.GetProperty("view").GetString();
            if (viewJson is null) return null;

            using var viewDoc = JsonDocument.Parse(viewJson);
            var series = viewDoc.RootElement.GetProperty("series");
            if (series.GetArrayLength() < 4) return null;

            var current  = BuildSession(series[0], series[1]);
            var previous = BuildSession(series[2], series[3]);

            logger.LogInformation(
                "Parsed chart: current={C} pts, previous={P} pts",
                current.Count, previous.Count);

            return new ChartData(current, previous);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse chart JSON");
            return null;
        }
    }

    private static List<InterbankRate> BuildSession(
        JsonElement sellSeries, JsonElement buySeries)
    {
        var sell = NonNullPoints(sellSeries);
        var buy  = NonNullPoints(buySeries);

        return sell.Keys
            .Where(ts => buy.ContainsKey(ts))
            .OrderBy(ts => ts)
            .Select(ts =>
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
                return new InterbankRate(time, buy[ts], sell[ts], ts);
            })
            .ToList();
    }

    private static SortedDictionary<long, decimal> NonNullPoints(JsonElement series)
    {
        var dict = new SortedDictionary<long, decimal>();
        foreach (var pt in series.GetProperty("data").EnumerateArray())
            if (pt[1].ValueKind != JsonValueKind.Null)
                dict[pt[0].GetInt64()] = pt[1].GetDecimal();
        return dict;
    }
}
