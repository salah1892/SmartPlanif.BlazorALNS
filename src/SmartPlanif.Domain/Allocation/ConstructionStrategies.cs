using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Allocation;

/// <summary>Bilan d'une phase de construction (un type de ligne).</summary>
public sealed record ConstructionPhase(
    long? LineTypeId, string Label, int TripCount, int PeakDemand, int CompatibleBuses, double Tension, int Assigned);

/// <summary>Plan initial et description des phases qui l'ont produit.</summary>
public sealed record ConstructionResult(AllocationState State, IReadOnlyList<ConstructionPhase> Phases);

/// <summary>Stratégie de construction d'un premier plan (heuristique constructive).</summary>
public interface IConstructionStrategy
{
    /// <summary>Construit un plan initial. <paramref name="lockedBook"/> contient déjà les voyages verrouillés ; l'état en prend possession.</summary>
    ConstructionResult Build(
        IReadOnlyList<Trip> trips, ResourcePool pool, ScheduleBook lockedBook,
        TripAllocator allocator, IReadOnlyDictionary<long, string> lineTypeLabels);
}

/// <summary>Heuristique gloutonne chronologique : base de comparaison la plus simple.</summary>
public sealed class ChronologicalConstructionStrategy : IConstructionStrategy
{
    /// <inheritdoc />
    public ConstructionResult Build(
        IReadOnlyList<Trip> trips, ResourcePool pool, ScheduleBook lockedBook,
        TripAllocator allocator, IReadOnlyDictionary<long, string> lineTypeLabels)
    {
        var state = new AllocationState(trips, lockedBook);
        foreach (var trip in trips.OrderBy(t => t.Departure).ThenBy(t => t.Id))
        {
            var outcome = allocator.TryAssign(trip, pool, state.Book);
            if (outcome.Assignment is not null) state.Assign(outcome.Assignment);
        }
        return new ConstructionResult(state, Array.Empty<ConstructionPhase>());
    }
}

/// <summary>
/// Construction par phases : les types de ligne sont traités par ordre de TENSION décroissante
/// (demande maximale simultanée / bus compatibles), donc les ressources rares vont d'abord aux voyages qui en ont
/// vraiment besoin. Dans chaque phase, les voyages les plus contraints (peu de candidats) passent en premier.
/// L'état (plannings) est partagé entre les phases : les agents consommés par une phase sont vus par la suivante.
/// </summary>
public sealed class PhasedConstructionStrategy : IConstructionStrategy
{
    /// <inheritdoc />
    public ConstructionResult Build(
        IReadOnlyList<Trip> trips, ResourcePool pool, ScheduleBook lockedBook,
        TripAllocator allocator, IReadOnlyDictionary<long, string> lineTypeLabels)
    {
        var state = new AllocationState(trips, lockedBook);
        var policy = allocator.Policy;

        // 1. Regrouper par type de ligne et mesurer la tension de chaque lot.
        var lots = trips.GroupBy(t => t.LineTypeId)
            .Select(g =>
            {
                var peak = FeasibilityAnalyzer.PeakConcurrent(g, policy.MinBusTurnaround);
                var compatible = FeasibilityAnalyzer.CountCompatibleBuses(pool, policy, g.Key);
                return (Key: g.Key, Trips: g.ToList(), Peak: peak, Compatible: compatible,
                        Tension: (double)peak / Math.Max(1, compatible));
            })
            .OrderByDescending(l => l.Tension).ThenByDescending(l => l.Trips.Count).ThenBy(l => l.Key)
            .ToList();

        // 2. Construire phase par phase sur le même état.
        var phases = new List<ConstructionPhase>();
        foreach (var lot in lots)
        {
            var ordered = lot.Trips
                .Select(t => (Trip: t, Difficulty: allocator.CountFeasibleOptions(t, pool, state.Book)))
                .OrderBy(x => x.Difficulty).ThenBy(x => x.Trip.Departure).ThenBy(x => x.Trip.Id)
                .Select(x => x.Trip);

            var assigned = 0;
            foreach (var trip in ordered)
            {
                var outcome = allocator.TryAssign(trip, pool, state.Book);
                if (outcome.Assignment is null) continue;
                state.Assign(outcome.Assignment);
                assigned++;
            }

            var label = lot.Key is { } id ? lineTypeLabels.GetValueOrDefault(id, $"Type {id}") : "Type inconnu";
            phases.Add(new ConstructionPhase(lot.Key, label, lot.Trips.Count, lot.Peak, lot.Compatible, lot.Tension, assigned));
        }
        return new ConstructionResult(state, phases);
    }
}
