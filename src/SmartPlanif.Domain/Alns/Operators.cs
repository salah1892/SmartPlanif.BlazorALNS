using SmartPlanif.Domain.Allocation;

namespace SmartPlanif.Domain.Alns;

// ====================================================================================================
//  OPÉRATEURS DE DESTRUCTION
// ====================================================================================================

/// <summary>Suppression aléatoire : diversification pure.</summary>
public sealed class RandomRemoval : IDestroyOperator
{
    /// <inheritdoc />
    public string Name => "Suppression aléatoire";

    /// <inheritdoc />
    public void Destroy(AllocationState state, int count, Random random)
    {
        var ids = state.Assignments.Keys.ToList();
        Shuffle(ids, random);
        foreach (var id in ids.Take(count)) state.Unassign(id);
    }

    internal static void Shuffle<T>(IList<T> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>Suppression des affectations les moins bonnes (plus grand écart au score maximal, avec un peu d'aléa).</summary>
public sealed class WorstScoreRemoval : IDestroyOperator
{
    /// <inheritdoc />
    public string Name => "Suppression des pires scores";

    /// <inheritdoc />
    public void Destroy(AllocationState state, int count, Random random)
    {
        var worst = state.Assignments.Values
            .OrderByDescending(a => (a.MaxScore - a.Score) * (0.5 + random.NextDouble()))
            .Take(count)
            .Select(a => a.Trip.Id)
            .ToList();
        foreach (var id in worst) state.Unassign(id);
    }
}

/// <summary>
/// Suppression par voisinage temporel : retire les voyages dont l'heure de départ est la plus proche d'un pivot.
/// Le pivot est souvent un voyage non affecté : on libère alors exactement ce qui le bloque (échange implicite).
/// </summary>
public sealed class TimeWindowRemoval : IDestroyOperator
{
    /// <inheritdoc />
    public string Name => "Voisinage temporel";

    /// <inheritdoc />
    public void Destroy(AllocationState state, int count, Random random)
    {
        if (state.Assignments.Count == 0) return;

        var unassigned = state.UnassignedTrips.ToList();
        var pivotTime = unassigned.Count > 0 && random.NextDouble() < 0.7
            ? unassigned[random.Next(unassigned.Count)].Departure
            : state.Assignments.Values.ElementAt(random.Next(state.Assignments.Count)).Trip.Departure;

        var nearest = state.Assignments.Values
            .OrderBy(a => Math.Abs((a.Trip.Departure - pivotTime).TotalMinutes))
            .Take(count)
            .Select(a => a.Trip.Id)
            .ToList();
        foreach (var id in nearest) state.Unassign(id);
    }
}

/// <summary>Suppression par ressource : vide une partie du planning d'un bus, d'un chauffeur ou d'un receveur choisi au hasard.</summary>
public sealed class ResourceRemoval : IDestroyOperator
{
    /// <inheritdoc />
    public string Name => "Suppression par ressource";

    /// <inheritdoc />
    public void Destroy(AllocationState state, int count, Random random)
    {
        if (state.Assignments.Count == 0) return;

        var removed = 0;
        var attempts = 0;
        while (removed < count && state.Assignments.Count > 0 && attempts++ < 10)
        {
            var seed = state.Assignments.Values.ElementAt(random.Next(state.Assignments.Count));
            var role = random.Next(3);   // 0 = bus, 1 = chauffeur, 2 = receveur

            var sameResource = state.Assignments.Values
                .Where(a => role == 0 ? a.Bus.Id == seed.Bus.Id
                          : role == 1 ? a.Driver.Id == seed.Driver.Id
                          : a.Receiver.Id == seed.Receiver.Id)
                .Select(a => a.Trip.Id)
                .Take(count - removed)
                .ToList();

            foreach (var id in sameResource)
                if (state.Unassign(id)) removed++;
        }
    }
}

// ====================================================================================================
//  OPÉRATEURS DE RÉPARATION
// ====================================================================================================

/// <summary>Base commune : insère les voyages dans l'ordre donné, avec le meilleur triplet faisable à chaque fois.</summary>
public abstract class RepairOperatorBase : IRepairOperator
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public void Repair(AllocationState state, RepairContext context)
    {
        var pending = state.UnassignedTrips.ToList();
        foreach (var trip in Order(pending, state, context))
        {
            var outcome = context.Allocator.TryAssign(trip, context.Pool, state.Book, explain: false, noiseSource: UseNoise ? context.Random : null);
            if (outcome.Assignment is not null) state.Assign(outcome.Assignment);
        }
    }

    /// <summary>Vrai si le classement des candidats doit être légèrement bruité (diversification).</summary>
    protected abstract bool UseNoise { get; }

    /// <summary>Ordre dans lequel les voyages en attente sont réinsérés.</summary>
    protected abstract IEnumerable<Entities.Trip> Order(List<Entities.Trip> pending, AllocationState state, RepairContext context);
}

/// <summary>Réinsertion chronologique, sans bruit : réparation « gloutonne » classique.</summary>
public sealed class ChronologicalRepair : RepairOperatorBase
{
    /// <inheritdoc />
    public override string Name => "Réparation chronologique";
    /// <inheritdoc />
    protected override bool UseNoise => false;
    /// <inheritdoc />
    protected override IEnumerable<Entities.Trip> Order(List<Entities.Trip> pending, AllocationState state, RepairContext context) =>
        pending.OrderBy(t => t.Departure).ThenBy(t => t.Id);
}

/// <summary>Réinsertion du plus contraint au moins contraint (peu de candidats éligibles d'abord), avec bruit.</summary>
public sealed class MostConstrainedFirstRepair : RepairOperatorBase
{
    /// <inheritdoc />
    public override string Name => "Plus contraint d'abord";
    /// <inheritdoc />
    protected override bool UseNoise => true;
    /// <inheritdoc />
    protected override IEnumerable<Entities.Trip> Order(List<Entities.Trip> pending, AllocationState state, RepairContext context) =>
        pending
            .Select(t => (Trip: t, Difficulty: context.Allocator.CountFeasibleOptions(t, context.Pool, state.Book)))
            .OrderBy(x => x.Difficulty).ThenBy(x => x.Trip.Departure).ThenBy(x => x.Trip.Id)
            .Select(x => x.Trip)
            .ToList();
}

/// <summary>Réinsertion dans un ordre aléatoire, avec bruit : diversification de la recherche.</summary>
public sealed class RandomOrderRepair : RepairOperatorBase
{
    /// <inheritdoc />
    public override string Name => "Ordre aléatoire";
    /// <inheritdoc />
    protected override bool UseNoise => true;
    /// <inheritdoc />
    protected override IEnumerable<Entities.Trip> Order(List<Entities.Trip> pending, AllocationState state, RepairContext context)
    {
        RandomRemoval.Shuffle(pending, context.Random);
        return pending;
    }
}
