using Dapper;
using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Allocation;

namespace SmartPlanif.Infrastructure.MySql;

/// <summary>Écrit des propositions (PROPOSED) dans trip_assignments ; trips.BUS_PR/CHAUFF_PR/REC_PR ne sont mis à jour qu'à la validation.</summary>
public sealed class MySqlAssignmentRepository(IDbConnectionFactory db) : IAssignmentRepository
{
    public async Task SaveProposalsAsync(IReadOnlyList<TripAssignment> assignments, string user, CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var rows = assignments.Select(a => new
        {
            TripId = a.Trip.Id, BusId = a.Bus.Id, DriverId = a.Driver.Id, ReceiverId = a.Receiver.Id,
            Score = Math.Round(a.Score, 2), a.Justification, User = user,
        });

        // L'index unique UX_ACTIVE_TRIP fait échouer l'insertion si le voyage a déjà une affectation active
        // (protection contre deux allocations concurrentes) -> toute la transaction est annulée.
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO trip_assignments (TRIP_ID, BUS_ID, DRIVER_ID, RECEIVER_ID, STATUS, SCORE, JUSTIFICATION, ASSIGNED_AT, ASSIGNED_BY)
            VALUES (@TripId, @BusId, @DriverId, @ReceiverId, 'PROPOSED', @Score, @Justification, UTC_TIMESTAMP(), @User)
            """, rows, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }
}
