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

            // Forex feeds have a single series (one price, no buy/sell split).
            if (series.GetArrayLength() == 1)
                return ParseSingleSeries(series[0], logger);

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

    /// <summary>
    /// Parses a single-series forex response. Uses the same value for Buy and Sell.
    /// Splits into two sessions: previous day(s) vs today, based on a &gt;6 hour gap.
    /// </summary>
    private static ChartData ParseSingleSeries(JsonElement series, ILogger logger)
    {
        var points = NonNullPoints(series);
        if (points.Count == 0)
            return ChartData.Empty;

        var all = points
            .Select(kv =>
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(kv.Key).LocalDateTime;
                return new InterbankRate(time, kv.Value, kv.Value, kv.Key);
            })
            .OrderBy(r => r.TimestampMs)
            .ToList();

        // Split sessions at the largest gap > 6 hours (overnight break)
        int splitIdx = 0;
        long maxGap = 0;
        const long sixHoursMs = 6L * 60 * 60 * 1000;
        for (int i = 1; i < all.Count; i++)
        {
            long gap = all[i].TimestampMs - all[i - 1].TimestampMs;
            if (gap > sixHoursMs && gap > maxGap)
            {
                maxGap = gap;
                splitIdx = i;
            }
        }

        List<InterbankRate> previous, current;
        if (splitIdx > 0)
        {
            previous = all.GetRange(0, splitIdx);
            current  = all.GetRange(splitIdx, all.Count - splitIdx);
        }
        else
        {
            previous = [];
            current  = all;
        }

        // Downsample to ~1 point per 30 minutes (keep last point in each bucket)
        previous = Downsample(previous, 30);
        current  = Downsample(current, 30);

        logger.LogInformation(
            "Parsed forex chart: current={C} pts, previous={P} pts",
            current.Count, previous.Count);

        return new ChartData(current, previous);
    }

    /// <summary>
    /// Keeps only the last data point in each N-minute bucket.
    /// </summary>
    private static List<InterbankRate> Downsample(List<InterbankRate> points, int minutesBucket)
    {
        if (points.Count == 0) return points;

        var bucketMs = (long)minutesBucket * 60 * 1000;
        return points
            .GroupBy(p => p.TimestampMs / bucketMs)
            .Select(g => g.Last())
            .OrderBy(p => p.TimestampMs)
            .ToList();
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
