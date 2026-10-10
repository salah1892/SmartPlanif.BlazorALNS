using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Alns;
using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Application.UseCases;

/// <summary>Stratégie d'allocation choisie par l'utilisateur.</summary>
public enum AllocationMethod
{
    /// <summary>Glouton chronologique : heuristique simple, sert de référence de comparaison.</summary>
    GreedyChronological,

    /// <summary>Construction par phases (type de ligne, du plus tendu au moins tendu), voyages les plus contraints d'abord.</summary>
    PhasedConstruction,

    /// <summary>Construction par phases puis amélioration par métaheuristique ALNS + recuit simulé.</summary>
    PhasedConstructionWithAlns,
}

/// <summary>Demande d'allocation d'une journée.</summary>
/// <param name="Date">Jour à planifier.</param>
/// <param name="User">Utilisateur à l'origine de la demande (traçabilité).</param>
/// <param name="Method">Stratégie.</param>
/// <param name="SaveProposals">Enregistrer les propositions en base (sinon simple simulation).</param>
/// <param name="PolicyOverride">Politique à utiliser à la place de la politique enregistrée (essais sans sauvegarder).</param>
public sealed record AllocateDayCommand(
    DateOnly Date, string User, AllocationMethod Method, bool SaveProposals, AllocationPolicy? PolicyOverride = null);

/// <summary>Synthèse de la métaheuristique.</summary>
public sealed record AlnsSummary(
    int Iterations, TimeSpan Elapsed, double InitialObjective, double FinalObjective,
    IReadOnlyList<ConvergencePoint> Convergence, IReadOnlyList<OperatorStatistic> Operators);

/// <summary>Rapport complet d'une allocation.</summary>
public sealed record AllocationReport(
    DateOnly Date, AllocationMethod Method,
    int TotalTrips, int AssignedCount, int UnassignedCount, TimeSpan Elapsed,
    double ConstructionObjective, double FinalObjective,
    IReadOnlyList<string> HardConstraintCodes, IReadOnlyList<string> SoftConstraintCodes,
    IReadOnlyList<TripAssignment> Assignments, IReadOnlyList<UnassignedTrip> Failures,
    IReadOnlyList<ConstructionPhase> Phases, IReadOnlyList<FeasibilityLine> Feasibility,
    IReadOnlyList<PlanViolation> Violations, AlnsSummary? Alns, bool ProposalsSaved);

/// <summary>
/// Cas d'usage principal : allouer bus, chauffeurs et receveurs aux voyages d'une journée.
/// Charge les données une seule fois, exécute l'algorithme hors du fil d'interface, contrôle le plan obtenu
/// puis, si demandé et si le plan est valide, enregistre les propositions.
/// </summary>
public sealed class AllocateDayHandler(
    ITripRepository trips, IResourceRepository resources, IAssignmentRepository assignments,
    IReferenceDataRepository reference, IAllocationSettingsStore settings)
{
    /// <summary>Exécute l'allocation. L'annulation (<paramref name="ct"/>) arrête proprement la métaheuristique.</summary>
    public async Task<AllocationReport> HandleAsync(AllocateDayCommand command, IProgress<AlnsProgress>? progress, CancellationToken ct)
    {
        var policy = command.PolicyOverride ?? await settings.LoadAsync(ct);

        // Chargement unique de toute la fenêtre J-1..J+1 (repos et voyages après minuit), aucune requête dans la boucle.
        var toAllocate = await trips.GetToAllocateAsync(command.Date, ct);
        var locked = await trips.GetAssignedAsync(command.Date.AddDays(-1), command.Date.AddDays(1), ct);
        var pool = await resources.GetPoolAsync(command.Date.AddDays(-1), command.Date.AddDays(1), ct);
        var labels = (await reference.GetLineTypesAsync(ct)).ToDictionary(x => x.Id, x => x.Label);

        var report = await Task.Run(() => Compute(command, policy, toAllocate, locked, pool, labels, progress, ct));

        // Garde-fou : un plan qui viole une règle dure n'est jamais enregistré.
        if (command.SaveProposals && report.Assignments.Count > 0 && report.Violations.Count == 0)
        {
            await assignments.SaveProposalsAsync(report.Assignments, command.User, ct);
            report = report with { ProposalsSaved = true };
        }
        return report;
    }

    /// <summary>Partie CPU de l'allocation (exécutée sur un fil d'arrière-plan).</summary>
    private static AllocationReport Compute(
        AllocateDayCommand command, AllocationPolicy policy,
        IReadOnlyList<Trip> toAllocate, IReadOnlyList<AssignedTrip> locked, ResourcePool pool,
        IReadOnlyDictionary<long, string> lineTypeLabels, IProgress<AlnsProgress>? progress, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var allocator = new TripAllocator(policy);

        // Les voyages déjà affectés occupent leurs ressources avant tout calcul.
        var book = new ScheduleBook();
        foreach (var a in locked) book.Commit(a.Trip, a.BusId, a.DriverId, a.ReceiverId);

        var feasibility = FeasibilityAnalyzer.Analyze(toAllocate, pool, policy, command.Date, lineTypeLabels);

        // Phase 1 : construction.
        IConstructionStrategy strategy = command.Method == AllocationMethod.GreedyChronological
            ? new ChronologicalConstructionStrategy()
            : new PhasedConstructionStrategy();
        var construction = strategy.Build(toAllocate, pool, book, allocator, lineTypeLabels);
        var objective = new ObjectiveFunction(policy, pool);
        var constructionObjective = objective.Evaluate(construction.State);
        var state = construction.State;

        // Phase 2 : amélioration par ALNS (optionnelle).
        AlnsSummary? alnsSummary = null;
        if (command.Method == AllocationMethod.PhasedConstructionWithAlns && toAllocate.Count > 0)
        {
            var result = new AlnsImprover().Improve(state, pool, allocator, progress, ct);
            state = result.Best;
            alnsSummary = new AlnsSummary(result.Iterations, result.Elapsed, result.InitialObjective,
                result.FinalObjective, result.Convergence, result.OperatorStatistics);
        }

        // Phase 3 : dernière chance déterministe pour les voyages restants, puis contrôles.
        PlanCompleter.TryCompleteUnassigned(state, pool, allocator);
        var finalObjective = objective.Evaluate(state);

        var finalAssignments = PlanEvaluator.BuildFinalAssignments(state, pool, allocator);
        var violations = PlanValidator.Validate(state, pool, allocator);

        var failures = state.UnassignedTrips
            .Select(t => allocator.TryAssign(t, pool, state.Book).Failure)
            .OfType<UnassignedTrip>()
            .OrderBy(f => f.Trip.Departure)
            .ToList();

        var hardCodes = allocator.BusRules.Hard.Select(r => r.Code)
            .Concat(allocator.AgentRules.Hard.Select(r => r.Code)).Distinct().OrderBy(c => c).ToList();
        var softCodes = allocator.BusRules.Soft.Select(r => r.Code)
            .Concat(allocator.AgentRules.Soft.Select(r => r.Code)).Distinct().OrderBy(c => c).ToList();

        return new AllocationReport(
            command.Date, command.Method, toAllocate.Count, finalAssignments.Count, failures.Count, stopwatch.Elapsed,
            constructionObjective, finalObjective, hardCodes, softCodes,
            finalAssignments, failures, construction.Phases, feasibility, violations, alnsSummary, false);
    }
}

/// <summary>Enregistrement des services de la couche Application.</summary>
public static class ApplicationServiceRegistration
{
    /// <summary>Ajoute les cas d'usage au conteneur d'injection de dépendances.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services
            .AddTransient<AllocateDayHandler>()
            .AddTransient<PreviewDayHandler>()
            .AddTransient<AllocationSettingsService>();
}
