using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Rules;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Allocation;

/// <summary>Violation détectée par le contrôle indépendant d'un plan.</summary>
public sealed record PlanViolation(long TripId, string Resource, string Code, string Message);

/// <summary>
/// Contrôle INDÉPENDANT de l'algorithme : rejoue toutes les règles dures actives sur le plan final.
/// Un plan produit par le moteur doit toujours être valide ; toute violation signale un bug.
/// </summary>
public static class PlanValidator
{
    /// <summary>Liste des violations (vide si le plan est valide).</summary>
    public static IReadOnlyList<PlanViolation> Validate(AllocationState state, ResourcePool pool, TripAllocator allocator)
    {
        var book = state.Book.Clone();
        var violations = new List<PlanViolation>();

        foreach (var a in state.Assignments.Values)
        {
            if (a.Driver.Role != AgentRole.Driver)
                violations.Add(new PlanViolation(a.Trip.Id, $"Chauffeur {a.Driver.Name}", "A02", "Qualification incorrecte (chauffeur)"));
            if (a.Receiver.Role != AgentRole.Receiver)
                violations.Add(new PlanViolation(a.Trip.Id, $"Receveur {a.Receiver.Name}", "A02", "Qualification incorrecte (receveur)"));
            if (a.Driver.Id == a.Receiver.Id)
                violations.Add(new PlanViolation(a.Trip.Id, $"Agent {a.Driver.Name}", "A06", "Même agent chauffeur et receveur"));

            CheckResource($"Bus {a.Bus.Plate}", a.Bus, a.Bus.Id, ResourceKind.Bus, book.Bus(a.Bus.Id), a.Trip, allocator.BusRules, allocator.Policy, pool, violations);
            CheckResource($"Chauffeur {a.Driver.Name}", a.Driver, a.Driver.Id, ResourceKind.Agent, book.Agent(a.Driver.Id), a.Trip, allocator.AgentRules, allocator.Policy, pool, violations);
            CheckResource($"Receveur {a.Receiver.Name}", a.Receiver, a.Receiver.Id, ResourceKind.Agent, book.Agent(a.Receiver.Id), a.Trip, allocator.AgentRules, allocator.Policy, pool, violations);
        }
        return violations;
    }

    private static void CheckResource<T>(
        string label, T resource, long id, ResourceKind kind, ResourceSchedule schedule, Trip trip,
        RuleSet<T> rules, AllocationPolicy policy, ResourcePool pool, List<PlanViolation> into)
    {
        schedule.Remove(trip);
        try
        {
            var context = new RuleContext<T>(resource, trip, schedule, pool.UnavailabilityFor(kind, id), policy);
            foreach (var rule in rules.Hard)
            {
                var violation = rule.Check(context);
                if (violation is not null) into.Add(new PlanViolation(trip.Id, label, violation.Code, violation.Message));
            }
        }
        finally
        {
            schedule.Add(trip);
        }
    }
}
