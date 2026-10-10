using Dapper;
using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Infrastructure.MySql;

/// <summary>
/// Lecture des voyages. ATTENTION : requêtes écrites d'après le schéma fourni mais NON testées sur vos données.
/// Points à confirmer avec la société : valeurs de trips.ETAT planifiables, définition d'un voyage « complet ».
/// </summary>
public sealed class MySqlTripRepository(IDbConnectionFactory db) : ITripRepository
{
    // L'heure d'arrivée est dérivée du dernier arrêt de stop_times ; ETAT = 3 signifie « terminé » (commentaire du schéma).
    private const string BaseSql = """
        SELECT t.ID AS Id, t.DENUMLI AS LineId,
               COALESCE(l.DENOMLI, CONCAT('Ligne ', t.DENUMLI)) AS LineLabel,
               l.DECTYLI AS LineTypeId,
               t.TIME_DEPART AS Departure,
               (SELECT MAX(s.ARRIVAL_TIME) FROM stop_times s WHERE s.TRIP_ID = t.ID) AS Arrival,
               COALESCE((SELECT MAX(s.SHAPE_DIST_TRAVELED) FROM stop_times s WHERE s.TRIP_ID = t.ID), l.DENBRKM, 0) AS DistanceKm,
               l.DECCENT AS CenterId, l.DECDELEG AS DelegationId,
               (SELECT s.DECSTAT FROM stop_times s WHERE s.TRIP_ID = t.ID ORDER BY s.STOP_SEQUENCE ASC  LIMIT 1) AS OriginStationId,
               (SELECT s.DECSTAT FROM stop_times s WHERE s.TRIP_ID = t.ID ORDER BY s.STOP_SEQUENCE DESC LIMIT 1) AS DestinationStationId,
               t.BUS_PR AS BusId, t.CHAUFF_PR AS DriverId, t.REC_PR AS ReceiverId
        FROM trips t
        JOIN drligne l ON l.DENUMLI = t.DENUMLI AND l.DEACTIF = 1
        WHERE t.TIME_DEPART >= @From AND t.TIME_DEPART < @To
          AND COALESCE(t.ETAT, 0) <> 3
        """;

    /// <summary>Ligne brute lue en base (propriétés modifiables, requis par Dapper).</summary>
    private sealed class Row
    {
        public long Id { get; set; }
        public long LineId { get; set; }
        public string LineLabel { get; set; } = "";
        public long? LineTypeId { get; set; }
        public DateTime Departure { get; set; }
        public DateTime? Arrival { get; set; }
        public double DistanceKm { get; set; }
        public long? CenterId { get; set; }
        public long? DelegationId { get; set; }
        public long? OriginStationId { get; set; }
        public long? DestinationStationId { get; set; }
        public long? BusId { get; set; }
        public long? DriverId { get; set; }
        public long? ReceiverId { get; set; }

        /// <summary>Convertit en voyage ; null si les horaires sont incohérents (règle V08), le voyage est alors écarté.</summary>
        public Trip? ToTrip() => Arrival is { } arrival && arrival > Departure
            ? new Trip(Id, LineId, LineLabel, LineTypeId, Departure, arrival, DistanceKm, CenterId, DelegationId,
                       OriginStationId, DestinationStationId, null)
            : null;
    }

    private async Task<List<Row>> QueryAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var connection = await db.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            BaseSql,
            new { From = from.ToDateTime(TimeOnly.MinValue), To = to.AddDays(1).ToDateTime(TimeOnly.MinValue) },
            cancellationToken: ct));
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Trip>> GetToAllocateAsync(DateOnly date, CancellationToken ct) =>
        (await QueryAsync(date, date, ct))
            .Where(r => r.BusId is null || r.DriverId is null || r.ReceiverId is null)
            .Select(r => r.ToTrip()).OfType<Trip>().ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssignedTrip>> GetAssignedAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        (await QueryAsync(from, to, ct))
            .Where(r => r.BusId is not null && r.DriverId is not null && r.ReceiverId is not null)
            .Select(r => r.ToTrip() is { } trip ? new AssignedTrip(trip, r.BusId!.Value, r.DriverId!.Value, r.ReceiverId!.Value) : null)
            .OfType<AssignedTrip>().ToList();
}
