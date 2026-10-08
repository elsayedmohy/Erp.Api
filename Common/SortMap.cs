using System.Linq.Expressions;

namespace ErpDashboard.Api.Common;

public sealed class SortMap<T>
{
    private readonly Dictionary<string, Func<IQueryable<T>, bool, IOrderedQueryable<T>>> _keys =
        new(StringComparer.OrdinalIgnoreCase);

    private Func<IOrderedQueryable<T>, IOrderedQueryable<T>> _tieBreaker = q => q;
    private string? _defaultKey;
    private bool _defaultDescending;

    public SortMap<T> Add<TKey>(string name, Expression<Func<T, TKey>> key)
    {
        _keys[name] = (q, desc) => desc ? q.OrderByDescending(key) : q.OrderBy(key);
        return this;
    }

    public SortMap<T> Default(string name, bool descending = false)
    {
        _defaultKey = name;
        _defaultDescending = descending;
        return this;
    }

    public SortMap<T> TieBreaker<TKey>(Expression<Func<T, TKey>> key)
    {
        _tieBreaker = q => q.ThenBy(key);
        return this;
    }

    public IOrderedQueryable<T> Apply(IQueryable<T> query, string? sortBy, string? sortDir)
    {
        var userChoseKey = !string.IsNullOrWhiteSpace(sortBy);
        var name = userChoseKey ? sortBy!.Trim() : _defaultKey
                                                   ?? throw new InvalidOperationException($"No default sort configured for {typeof(T).Name}.");

        if (!_keys.TryGetValue(name, out var apply))
            throw new BadRequestException(
                $"Invalid sortBy '{sortBy}'. Allowed: {string.Join(", ", _keys.Keys)}.");

        var descending = sortDir?.Trim().ToLowerInvariant() switch
        {
            null or "" => !userChoseKey && _defaultDescending,
            "asc" => false,
            "desc" => true,
            _ => throw new BadRequestException("sortDir must be 'asc' or 'desc'.")
        };

        return _tieBreaker(apply(query, descending));
    }
}