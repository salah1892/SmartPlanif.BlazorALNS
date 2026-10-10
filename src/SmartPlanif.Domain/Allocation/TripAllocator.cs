using SmartPlanif.Domain.Constraints;
using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Rules;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Allocation;

/// <summary>Affectation complète d'un voyage : bus, chauffeur, receveur, score et justification.</summary>
public sealed record TripAssignment(Trip Trip, Bus Bus, Agent Driver, Agent Receiver, double Score, double MaxScore, string Justification);

/// <summary>Voyage non affecté : rôles manquants et causes de rejet (code de règle -> nombre de ressources rejetées).</summary>
public sealed record UnassignedTrip(Trip Trip, IReadOnlyList<string> MissingRoles, IReadOnlyDictionary<string, int> RejectionsByCode);

/// <summary>Résultat de la tentative d'affectation d'un voyage : soit une affectation, soit un échec expliqué.</summary>
public sealed record TripAllocationOutcome(TripAssignment? Assignment, UnassignedTrip? Failure);

/// <summary>Résultat d'une allocation gloutonne simple.</summary>
public sealed record AllocationResult(IReadOnlyList<TripAssignment> Assignments, IReadOnlyList<UnassignedTrip> Unassigned);

/// <summary>
/// Cœur de décision pour UN voyage : applique les règles dures, score les survivants, choisit le meilleur triplet
/// (bus, chauffeur, receveur). Les règles appliquées dépendent des choix de la politique (catalogue de contraintes).
/// Cette classe ne modifie JAMAIS les plannings : c'est à l'appelant d'enregistrer l'affectation.
/// </summary>
public sealed class TripAllocator
{
    /// <summary>Crée l'allocateur et construit les jeux de règles selon la politique.</summary>
    public TripAllocator(AllocationPolicy policy)
    {
        Policy = policy;
        BusRules = ConstraintCatalog.BuildBusRuleSet(policy);
        AgentRules = ConstraintCatalog.BuildAgentRuleSet(policy);
    }

    /// <summary>Politique utilisée.</summary>
    public AllocationPolicy Policy { get; }

    /// <summary>Règles applicables aux bus.</summary>
    public RuleSet<Bus> BusRules { get; }

    /// <summary>Règles applicables aux agents (chauffeurs et receveurs).</summary>
    public RuleSet<Agent> AgentRules { get; }

    /// <summary>Score maximal théorique d'un voyage : bus + chauffeur + receveur.</summary>
    public double MaxScorePerTrip => BusRules.MaxScore + 2 * AgentRules.MaxScore;

    /// <summary>
    /// Cherche le meilleur triplet pour un voyage. Tout ou rien : sans bus, chauffeur OU receveur valide,
    /// le voyage reste non affecté et les causes de rejet sont renvoyées.
    /// </summary>
    /// <param name="explain">Construit le texte de justification (inutile pendant la recherche métaheuristique).</param>
    /// <param name="noiseSource">Source aléatoire pour diversifier le classement (métaheuristique) ; null = déterministe.</param>
    public TripAllocationOutcome TryAssign(Trip trip, ResourcePool pool, ScheduleBook book, bool explain = false, Random? noiseSource = null)
    {
        var noise = noiseSource is null ? 0 : Policy.Alns.InsertionNoise;

        var buses = CandidateSelector.Select(BuildBusCandidates(pool, book), trip, BusRules, Policy, noiseSource, noise);
        var drivers = CandidateSelector.Select(BuildAgentCandidates(pool, book, AgentRole.Driver), trip, AgentRules, Policy, noiseSource, noise);
        var receivers = CandidateSelector.Select(BuildAgentCandidates(pool, book, AgentRole.Receiver), trip, AgentRules, Policy, noiseSource, noise);

        var bus = buses.Best;
        var driver = drivers.Best;
        var receiver = receivers.Candidates.FirstOrDefault(c => c.Id != driver?.Id);   // chauffeur != receveur

        if (bus is null || driver is null || receiver is null)
        {
            var missing = new List<string>();
            if (bus is null) missing.Add("BUS");
            if (driver is null) missing.Add("DRIVER");
            if (receiver is null) missing.Add("RECEIVER");

            var rejections = new Dictionary<string, int>();
            foreach (var source in new[] { buses.RejectionsByCode, drivers.RejectionsByCode, receivers.RejectionsByCode })
                foreach (var (code, count) in source)
                    rejections[code] = rejections.GetValueOrDefault(code) + count;

            return new TripAllocationOutcome(null, new UnassignedTrip(trip, missing, rejections));
        }

        var justification = explain
            ? string.Join(" | ",
                JustificationFormatter.Explain("Bus", bus.Id, bus.Score, bus.Details),
                JustificationFormatter.Explain("Chauffeur", driver.Id, driver.Score, driver.Details),
                JustificationFormatter.Explain("Receveur", receiver.Id, receiver.Score, receiver.Details))
            : string.Empty;

        var assignment = new TripAssignment(trip, bus.Resource, driver.Resource, receiver.Resource,
            bus.Score + driver.Score + receiver.Score, MaxScorePerTrip, justification);
        return new TripAllocationOutcome(assignment, null);
    }

    /// <summary>
    /// Difficulté d'un voyage : plus petit nombre de candidats éligibles parmi les trois rôles.
    /// Plus la valeur est faible, plus le voyage est contraint et doit être traité tôt.
    /// </summary>
    public int CountFeasibleOptions(Trip trip, ResourcePool pool, ScheduleBook book)
    {
        var buses = CandidateSelector.CountFeasible(BuildBusCandidates(pool, book), trip, BusRules, Policy);
        var drivers = CandidateSelector.CountFeasible(BuildAgentCandidates(pool, book, AgentRole.Driver), trip, AgentRules, Policy);
        var receivers = CandidateSelector.CountFeasible(BuildAgentCandidates(pool, book, AgentRole.Receiver), trip, AgentRules, Policy);
        return Math.Min(buses, Math.Min(drivers, receivers));
    }

    /// <summary>Allocation gloutonne chronologique simple (utile pour les tests et comme point de comparaison).</summary>
    public AllocationResult Allocate(IEnumerable<Trip> trips, ResourcePool pool, ScheduleBook book)
    {
        var assignments = new List<TripAssignment>();
        var failures = new List<UnassignedTrip>();

        foreach (var trip in trips.OrderBy(t => t.Departure).ThenBy(t => t.Id))
        {
            var outcome = TryAssign(trip, pool, book, explain: true);
            if (outcome.Assignment is { } a)
            {
                book.Commit(trip, a.Bus.Id, a.Driver.Id, a.Receiver.Id);   // le voyage suivant voit ce choix
                assignments.Add(a);
            }
            else failures.Add(outcome.Failure!);
        }
        return new AllocationResult(assignments, failures);
    }

    private static IEnumerable<Candidate<Bus>> BuildBusCandidates(ResourcePool pool, ScheduleBook book) =>
        pool.Buses.Select(b => new Candidate<Bus>(b.Id, b, book.Bus(b.Id), pool.UnavailabilityFor(ResourceKind.Bus, b.Id)));

    private static IEnumerable<Candidate<Agent>> BuildAgentCandidates(ResourcePool pool, ScheduleBook book, AgentRole role) =>
        pool.Agents.Where(a => a.Role == role)    // qualification (A01/A02) : on ne présente que les agents du bon rôle
            .Select(a => new Candidate<Agent>(a.Id, a, book.Agent(a.Id), pool.UnavailabilityFor(ResourceKind.Agent, a.Id)));
}
