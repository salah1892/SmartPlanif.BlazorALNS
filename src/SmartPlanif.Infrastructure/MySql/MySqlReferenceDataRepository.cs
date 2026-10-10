using Dapper;
using SmartPlanif.Application.Abstractions;

namespace SmartPlanif.Infrastructure.MySql;

/// <summary>Lecture des référentiels drtyli (types de ligne) et drcatve (catégories de véhicules).</summary>
public sealed class MySqlReferenceDataRepository(IDbConnectionFactory db) : IReferenceDataRepository
{
    private sealed class Row
    {
        public long Id { get; set; }
        public string? Label { get; set; }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReferenceItem>> GetLineTypesAsync(CancellationToken ct) =>
        QueryAsync("SELECT DECTYLI AS Id, DELTYLI AS Label FROM drtyli ORDER BY DECTYLI", "Type de ligne", ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<ReferenceItem>> GetBusCategoriesAsync(CancellationToken ct) =>
        QueryAsync("SELECT DECATVH AS Id, DECATEG AS Label FROM drcatve ORDER BY DECATVH", "Catégorie", ct);

    private async Task<IReadOnlyList<ReferenceItem>> QueryAsync(string sql, string fallbackPrefix, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => new ReferenceItem(r.Id, string.IsNullOrWhiteSpace(r.Label) ? $"{fallbackPrefix} {r.Id}" : r.Label!)).ToList();
    }
}
