using Dapper;
using Microsoft.Extensions.Options;
using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Common;
using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Infrastructure.MySql;

/// <summary>
/// ATTENTION : dans le dump, le commentaire de dragent.DECQUAL est "Qualification (H=homme)".
/// Il faut confirmer avec la société quelle colonne distingue réellement chauffeur / receveur.
/// Le mapping est donc configurable (QualificationOptions) et non codé en dur.
/// </summary>
public sealed class QualificationOptions
{
    /// <summary>valeur de DECQUAL -> rôle. Exemple : { "C": Driver, "R": Receiver } (valeurs à confirmer).</summary>
    public Dictionary<string, AgentRole> Map { get; set; } = new();
}

public sealed class MySqlResourceRepository(IDbConnectionFactory db, IOptions<QualificationOptions> qualifications) : IResourceRepository
{
    private sealed class BusRow
    {
        public long Id { get; set; } public string? Plate { get; set; } public long Active { get; set; }
        public long Seats { get; set; } public long Standing { get; set; } public long? CategoryId { get; set; }
        public long? CenterId { get; set; } public long? DelegationId { get; set; } public DateTime? InServiceSince { get; set; }
    }
    private sealed class AgentRow
    {
        public long Id { get; set; } public string? Name { get; set; } public string? Qual { get; set; }
        public long? RestDay { get; set; } public long? CenterId { get; set; } public long? DelegationId { get; set; }
    }
    private sealed class UnavRow
    {
        public string Kind { get; set; } = ""; public long ResourceId { get; set; }
        public DateTime StartAt { get; set; } public DateTime EndAt { get; set; } public string? Reason { get; set; }
    }

    public async Task<ResourcePool> GetPoolAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);

        // drvehic.DECCENT / DECDELEG sont des DOUBLE alors que dragent/drligne utilisent des BIGINT : on convertit.
        var buses = (await conn.QueryAsync<BusRow>(new CommandDefinition("""
            SELECT DECODVH AS Id, DEMATRI AS Plate, COALESCE(DEACTIF, 0) AS Active,
                   COALESCE(DENBRPA, 0) AS Seats, COALESCE(DENBRPD, 0) AS Standing, DECATVH AS CategoryId,
                   CAST(DECCENT AS SIGNED) AS CenterId, CAST(DECDELEG AS SIGNED) AS DelegationId, DEDATEC AS InServiceSince
            FROM drvehic
            """, cancellationToken: ct)))
            .Select(r => new Bus(r.Id, r.Plate ?? $"#{r.Id}", r.Active == 1, (int)r.Seats, (int)r.Standing,
                                 r.CategoryId, r.CenterId, r.DelegationId, r.InServiceSince))
            .ToList();

        var map = qualifications.Value.Map;
        var agents = (await conn.QueryAsync<AgentRow>(new CommandDefinition("""
            SELECT DECAGEN AS Id, DENAGEN AS Name, DECQUAL AS Qual, DECJOUR AS RestDay, DECCENT AS CenterId, DECDELEG AS DelegationId
            FROM dragent
            """, cancellationToken: ct)))
            .Select(r => new Agent(r.Id, r.Name ?? $"#{r.Id}",
                                   r.Qual is not null && map.TryGetValue(r.Qual, out var role) ? role : AgentRole.Other,
                                   ToDayOfWeek(r.RestDay), r.CenterId, r.DelegationId))
            .ToList();

        var unav = (await conn.QueryAsync<UnavRow>(new CommandDefinition("""
            SELECT KIND AS Kind, RESOURCE_ID AS ResourceId, START_AT AS StartAt, END_AT AS EndAt, REASON AS Reason
            FROM resource_unavailability
            WHERE END_AT > @From AND START_AT < @To
            """, new { From = from.ToDateTime(TimeOnly.MinValue), To = to.AddDays(1).ToDateTime(TimeOnly.MinValue) },
            cancellationToken: ct)))
            .Select(r => new Unavailability(r.Kind == "BUS" ? ResourceKind.Bus : ResourceKind.Agent,
                                            r.ResourceId, new TimeInterval(r.StartAt, r.EndAt), r.Reason ?? ""))
            .ToList();

        return new ResourcePool(buses, agents, unav);
    }

    /// <summary>DECJOUR : 1=lundi..7=dimanche. DayOfWeek .NET : Sunday=0, Monday=1..Saturday=6 -> modulo 7.</summary>
    private static DayOfWeek? ToDayOfWeek(long? decjour) =>
        decjour is >= 1 and <= 7 ? (DayOfWeek)(decjour.Value % 7) : null;
}
