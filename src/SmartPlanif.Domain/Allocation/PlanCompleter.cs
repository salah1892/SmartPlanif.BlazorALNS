using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Allocation;

/// <summary>
/// Passe finale déterministe : tente d'affecter les voyages restés sans ressource, du plus contraint au moins contraint.
/// Utile après la métaheuristique (dont les réparations sont bruitées) : on ne laisse jamais un voyage affectable de côté.
/// </summary>
public static class PlanCompleter
{
    /// <summary>Renvoie le nombre de voyages supplémentaires affectés.</summary>
    public static int TryCompleteUnassigned(AllocationState state, ResourcePool pool, TripAllocator allocator)
    {
        var added = 0;
        var ordered = state.UnassignedTrips
            .Select(t => (Trip: t, Difficulty: allocator.CountFeasibleOptions(t, pool, state.Book)))
            .OrderBy(x => x.Difficulty).ThenBy(x => x.Trip.Departure).ThenBy(x => x.Trip.Id)
            .Select(x => x.Trip)
            .ToList();

        foreach (var trip in ordered)
        {
            var outcome = allocator.TryAssign(trip, pool, state.Book);
            if (outcome.Assignment is null) continue;
            state.Assign(outcome.Assignment);
            added++;
        }
        return added;
    }
}
