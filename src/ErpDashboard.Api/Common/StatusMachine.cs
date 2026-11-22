namespace ErpDashboard.Api.Common;

public sealed class StatusMachine<TStatus> where TStatus : struct, Enum
{
    private readonly IReadOnlyDictionary<TStatus, TStatus[]> _next;
    private readonly string _subject;

    // subject reads in a sentence: "order", "invoice", "purchase order".
    public StatusMachine(string subject, IReadOnlyDictionary<TStatus, TStatus[]> next)
    {
        var missing = Enum.GetValues<TStatus>().Where(s => !next.ContainsKey(s)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The {subject} status machine has no row for: {string.Join(", ", missing)}.");

        _subject = subject;
        _next = next;
    }

    public IReadOnlyList<TStatus> AllowedFrom(TStatus from) => _next[from];

    public bool CanMove(TStatus from, TStatus to) => _next[from].Contains(to);

    public void EnsureCanMove(TStatus from, TStatus to)
    {
        if (CanMove(from, to))
            return;

        var allowed = _next[from];
        throw new ConflictException(
            allowed.Length == 0
                ? $"This {_subject} is {from} and cannot change status."
                : $"Cannot change the {_subject} from {from} to {to}. Allowed: {string.Join(", ", allowed)}.",
            code: "invalid_transition",
            extensions: new Dictionary<string, object?>
            {
                ["currentStatus"] = from.ToString(),
                ["allowedTransitions"] = allowed.Select(s => s.ToString()).ToArray()
            });
    }
}