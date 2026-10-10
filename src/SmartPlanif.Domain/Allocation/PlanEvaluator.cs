using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Rules;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Allocation;

/// <summary>
/// Recalcule les scores EXACTS d'un plan terminé : pour chaque ressource, on retire temporairement le voyage de son
/// planning puis on évalue les critères souples dans le même contexte qu'au moment du choix.
/// </summary>
public static class PlanEvaluator
{
    /// <summary>Affectations finales avec score exact et justification, triées par heure de départ.</summary>
    public static IReadOnlyList<TripAssignment> BuildFinalAssignments(AllocationState state, ResourcePool pool, TripAllocator allocator)
    {
        var book = state.Book.Clone();    // on travaille sur une copie : l'état d'origine reste intact
        var result = new List<TripAssignment>();

        foreach (var a in state.Assignments.Values.OrderBy(x => x.Trip.Departure).ThenBy(x => x.Trip.Id))
        {
            var bus = ScoreResource("Bus", a.Bus, a.Bus.Id, ResourceKind.Bus, book.Bus(a.Bus.Id), a.Trip, allocator.BusRules, allocator.Policy, pool);
            var driver = ScoreResource("Chauffeur", a.Driver, a.Driver.Id, ResourceKind.Agent, book.Agent(a.Driver.Id), a.Trip, allocator.AgentRules, allocator.Policy, pool);
            var receiver = ScoreResource("Receveur", a.Receiver, a.Receiver.Id, ResourceKind.Agent, book.Agent(a.Receiver.Id), a.Trip, allocator.AgentRules, allocator.Policy, pool);

            result.Add(a with
            {
                Score = bus.Score + driver.Score + receiver.Score,
                MaxScore = allocator.MaxScorePerTrip,
                Justification = string.Join(" | ", bus.Text, driver.Text, receiver.Text),
            });
        }
        return result;
    }

    private static (double Score, string Text) ScoreResource<T>(
        string label, T resource, long id, ResourceKind kind, ResourceSchedule schedule,
        Trip trip, RuleSet<T> rules, AllocationPolicy policy, ResourcePool pool)
    {
        schedule.Remove(trip);   // le voyage ne doit pas se compter lui-même
        try
        {
            var context = new RuleContext<T>(resource, trip, schedule, pool.UnavailabilityFor(kind, id), policy);
            var details = CandidateSelector.ComputeScoreDetails(context, rules);
            var score = details.Sum(d => d.Points);
            return (score, JustificationFormatter.Explain(label, id, score, details));
        }
        finally
        {
            schedule.Add(trip);
        }
    }
}
