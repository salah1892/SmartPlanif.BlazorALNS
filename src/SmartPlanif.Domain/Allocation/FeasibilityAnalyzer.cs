using SmartPlanif.Domain.Constraints;
using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Allocation;

/// <summary>Ligne du diagnostic de faisabilité : demande minimale vs ressources disponibles.</summary>
public sealed record FeasibilityLine(string Scope, int Demand, int Supply, bool IsSufficient, string Message);

/// <summary>
/// Phase 0 : diagnostic des conditions NÉCESSAIRES pour tout planifier. Si une ligne est insuffisante,
/// aucun algorithme (heuristique ou non) ne pourra affecter tous les voyages : mieux vaut le savoir avant de chercher.
/// Ces bornes sont indicatives (elles ne remplacent pas l'allocation).
/// </summary>
public static class FeasibilityAnalyzer
{
    /// <summary>Nombre maximal de voyages simultanés, chaque voyage étant prolongé de <paramref name="extraGap"/> (repos / remise en service).</summary>
    public static int PeakConcurrent(IEnumerable<Trip> trips, TimeSpan extraGap)
    {
        var events = new List<(DateTime Time, int Delta)>();
        foreach (var trip in trips)
        {
            events.Add((trip.Departure, +1));
            events.Add((trip.Arrival + extraGap, -1));
        }

        var current = 0;
        var peak = 0;
        // À égalité de temps, les fins (-1) passent avant les débuts (+1) : une ressource libérée est réutilisable.
        foreach (var e in events.OrderBy(x => x.Time).ThenBy(x => x.Delta))
        {
            current += e.Delta;
            if (current > peak) peak = current;
        }
        return peak;
    }

    /// <summary>Bus actifs pouvant servir un type de ligne (en tenant compte de la compatibilité de catégorie si elle est dure).</summary>
    public static int CountCompatibleBuses(ResourcePool pool, AllocationPolicy policy, long? lineTypeId)
    {
        var activeBuses = pool.Buses.Where(b => b.Active);
        var categoryDescriptor = ConstraintCatalog.FindDescriptor("B06");
        var categoryIsHard = categoryDescriptor is not null
            && ConstraintCatalog.ResolveSetting(policy, categoryDescriptor).Mode == ConstraintMode.Hard;

        var rule = policy.FindCategoryRule(lineTypeId);
        if (!categoryIsHard || rule is null || rule.AllowedCategoryIds.Count == 0) return activeBuses.Count();
        return activeBuses.Count(b => b.CategoryId is { } c && rule.AllowedCategoryIds.Contains(c));
    }

    /// <summary>Diagnostic complet pour un jeu de voyages.</summary>
    public static IReadOnlyList<FeasibilityLine> Analyze(
        IReadOnlyList<Trip> trips, ResourcePool pool, AllocationPolicy policy, DateOnly date,
        IReadOnlyDictionary<long, string> lineTypeLabels)
    {
        var lines = new List<FeasibilityLine>();
        if (trips.Count == 0) return lines;

        // 1. Bus : pic de voyages simultanés vs bus actifs.
        var busDemand = PeakConcurrent(trips, policy.MinBusTurnaround);
        var busSupply = pool.Buses.Count(b => b.Active);
        lines.Add(Line("Bus (tous types de ligne)", busDemand, busSupply, "bus simultanés"));

        // 2. Bus par type de ligne : tient compte de la compatibilité de catégorie.
        foreach (var group in trips.GroupBy(t => t.LineTypeId).OrderBy(g => g.Key))
        {
            var label = group.Key is { } id ? lineTypeLabels.GetValueOrDefault(id, $"Type {id}") : "Type inconnu";
            var demand = PeakConcurrent(group, policy.MinBusTurnaround);
            var supply = CountCompatibleBuses(pool, policy, group.Key);
            lines.Add(Line($"Bus compatibles — {label}", demand, supply, "bus simultanés"));
        }

        // 3. Agents : pic simultané et charge horaire totale.
        foreach (var (role, roleLabel) in new[] { (AgentRole.Driver, "Chauffeurs"), (AgentRole.Receiver, "Receveurs") })
        {
            var available = pool.Agents.Count(a => a.Role == role && a.RestDay != date.DayOfWeek);
            lines.Add(Line($"{roleLabel} (simultanés)", PeakConcurrent(trips, policy.MinAgentRest), available, "agents simultanés"));

            var hoursDemand = (int)Math.Ceiling(trips.Sum(t => t.Interval.Duration.TotalHours));
            var hoursSupply = (int)Math.Floor(available * policy.MaxDailyWork.TotalHours);
            lines.Add(Line($"{roleLabel} (heures de travail)", hoursDemand, hoursSupply, "heures"));
        }
        return lines;
    }

    private static FeasibilityLine Line(string scope, int demand, int supply, string unit)
    {
        var ok = demand <= supply;
        var message = ok
            ? $"Suffisant : {demand} {unit} requis, {supply} disponibles."
            : $"INSUFFISANT : {demand} {unit} requis, seulement {supply} disponibles.";
        return new FeasibilityLine(scope, demand, supply, ok, message);
    }
}
