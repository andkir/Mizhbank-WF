namespace MizhbankNotifier.Models;

public record ChartData(
    IReadOnlyList<InterbankRate> CurrentSession,
    IReadOnlyList<InterbankRate> PreviousSession)
{
    public static readonly ChartData Empty = new([], []);

    /// <summary>Most recent session that has data.</summary>
    public IReadOnlyList<InterbankRate> LatestSession =>
        CurrentSession.Count > 0 ? CurrentSession : PreviousSession;

    public bool HasData => CurrentSession.Count > 0 || PreviousSession.Count > 0;
}
