using System.Text.RegularExpressions;
using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// Weighted relevance search for APT packages.
///
/// Scoring weights (per matched term):
///   Exact package name match  → 1000
///   Package name prefix match → 100
///   Package name contains     → 10
///   Description contains      → 1
///
/// Single-term searches are fully translated to SQL (CASE WHEN scoring,
/// ORDER BY score, OFFSET/LIMIT pagination — no data pulled into memory).
/// Multi-term searches use SQL to pre-filter then score in memory.
/// </summary>
public static class PackageSearchService
{
    public static async Task<(List<AptPackage> Items, int TotalCount)> SearchAsync(
        IQueryable<AptPackage> baseQuery,
        string keyword,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var terms = SplitTerms(keyword);
        if (terms.Length == 0) return ([], 0);

        return terms.Length == 1
            ? await SingleTermSqlSearch(baseQuery, terms[0], page, pageSize, ct)
            : await MultiTermHybridSearch(baseQuery, terms, page, pageSize, ct);
    }

    /// <summary>
    /// Single-term path: scoring expression is fully translated to SQL.
    /// Produces a query like:
    ///   SELECT *, (CASE WHEN LOWER(Package) = LOWER(@t) THEN 1000 ELSE 0 END
    ///            + CASE WHEN Package LIKE @t% THEN 100 ELSE 0 END
    ///            + CASE WHEN Package LIKE %@t% THEN 10 ELSE 0 END
    ///            + CASE WHEN Description LIKE %@t% THEN 1 ELSE 0 END) AS Score
    ///   FROM AptPackages WHERE ...
    ///   ORDER BY Score DESC, Package
    ///   LIMIT @pageSize OFFSET @skip
    /// </summary>
    private static async Task<(List<AptPackage> Items, int TotalCount)> SingleTermSqlSearch(
        IQueryable<AptPackage> baseQuery,
        string term,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var termLower = term.ToLower();
        var scoreQuery = baseQuery
            .Where(p => p.Package.Contains(term) || p.Description.Contains(term))
            .Select(p => new
            {
                Package = p,
                Score =
                    // Exact match: user typed the exact package name (case-insensitive)
                    (p.Package.ToLower() == termLower ? 1000 : 0)
                    // Prefix match: e.g. "snapd" starts with "snap"
                    + (p.Package.StartsWith(term) ? 100 : 0)
                    // Package name contains the term anywhere
                    + (p.Package.Contains(term) ? 10 : 0)
                    // Description mentions the term
                    + (p.Description.Contains(term) ? 1 : 0)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Package.Package);

        var total = await scoreQuery.CountAsync(ct);
        var items = await scoreQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Package)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <summary>
    /// Multi-term path: SQL filters candidates (any term in name or description),
    /// then in-memory scoring sums weighted hits per term.
    /// </summary>
    private static async Task<(List<AptPackage> Items, int TotalCount)> MultiTermHybridSearch(
        IQueryable<AptPackage> baseQuery,
        string[] terms,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        // EF Core translates terms.Any(t => p.Field.Contains(t)) to
        // (p.Field LIKE '%t1%' OR p.Field LIKE '%t2%' OR ...)
        var filtered = await baseQuery
            .Where(p => terms.Any(t => p.Package.Contains(t))
                     || terms.Any(t => p.Description.Contains(t)))
            .AsNoTracking()
            .ToListAsync(ct);

        var scored = filtered
            .Select(p => (Package: p, Score: ComputeScore(p, terms)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Package.Package)
            .ToList();

        var total = scored.Count;
        var items = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Package)
            .ToList();

        return (items, total);
    }

    private static int ComputeScore(AptPackage p, string[] terms) =>
        terms.Sum(term =>
            (p.Package.Equals(term, StringComparison.OrdinalIgnoreCase) ? 1000 : 0)
            + (p.Package.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 100 : 0)
            + (p.Package.Contains(term, StringComparison.OrdinalIgnoreCase) ? 10 : 0)
            + (p.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0));

    /// <summary>
    /// Pure in-memory scoring and ranking — database-free, suitable for unit testing.
    /// Applies the same weights used by the SQL and hybrid search paths.
    /// </summary>
    public static List<AptPackage> ScoreAndRank(IEnumerable<AptPackage> packages, string keyword)
    {
        var terms = SplitTerms(keyword);
        if (terms.Length == 0) return [];

        return packages
            .Select(p => (Package: p, Score: ComputeScore(p, terms)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Package.Package)
            .Select(x => x.Package)
            .ToList();
    }

    public static string[] SplitTerms(string keyword) =>
        Regex.Split(keyword.Trim(), @"\s+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
}
