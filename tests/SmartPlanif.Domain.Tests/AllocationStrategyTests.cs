using SmartPlanif.Domain.Alns;
using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Constraints;
using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Scheduling;
using Xunit;
using static SmartPlanif.Domain.Tests.TestData;

namespace SmartPlanif.Domain.Tests;

/// <summary>Construction gloutonne / par phases, ALNS, validateur et diagnostic de faisabilité.</summary>
public class AllocationStrategyTests
{
    /// <summary>Jeu aléatoire reproductible, avec des ressources volontairement un peu justes.</summary>
    private static (List<Trip> Trips, ResourcePool Pool) RandomScenario(int tripCount, int seed)
    {
        var random = new Random(seed);
        var trips = Enumerable.Range(1, tripCount).Select(i =>
        {
            var start = 5 + random.NextDouble() * 14;
            return Trip(i, start, start + 0.5 + random.NextDouble() * 2.5, center: 1 + i % 2, line: 10 + i % 5, lineType: 1 + i % 2);
        }).ToList();

        var buses = Enumerable.Range(1, 9).Select(i => Bus(i, center: 1 + i % 2, category: 1 + i % 2)).ToList();
        var agents = Enumerable.Range(1, 9).Select(i => Agent(i, AgentRole.Driver, center: 1 + i % 2))
            .Concat(Enumerable.Range(11, 9).Select(i => Agent(i, AgentRole.Receiver, center: 1 + i % 2))).ToList();
        return (trips, Pool(buses, agents));
    }

    [Fact]
    public void Greedy_result_is_deterministic()
    {
        var (trips, pool) = RandomScenario(30, 7);
        var allocator = new TripAllocator(Policy());
        string Signature(AllocationResult r) =>
            string.Join(";", r.Assignments.Select(a => $"{a.Trip.Id}:{a.Bus.Id}/{a.Driver.Id}/{a.Receiver.Id}"));

        Assert.Equal(Signature(allocator.Allocate(trips, pool, new ScheduleBook())),
                     Signature(allocator.Allocate(trips, pool, new ScheduleBook())));
    }

    [Fact]
    public void Plan_never_has_overlapping_trips_for_any_resource()
    {
        var (trips, pool) = RandomScenario(60, 42);
        var result = new TripAllocator(Policy()).Allocate(trips, pool, new ScheduleBook());

        var usage = result.Assignments.SelectMany(a => new[]
        {
            (Key: $"bus{a.Bus.Id}", a.Trip), (Key: $"agent{a.Driver.Id}", a.Trip), (Key: $"agent{a.Receiver.Id}", a.Trip),
        }).GroupBy(x => x.Key);

        foreach (var group in usage)
        {
            var ordered = group.Select(x => x.Trip).OrderBy(t => t.Departure).ToList();
            for (var i = 1; i < ordered.Count; i++)
                Assert.True(ordered[i].Departure >= ordered[i - 1].Arrival, $"Chevauchement pour {group.Key}");
        }
    }

    [Fact]
    public void Phased_construction_gives_scarce_comfort_bus_to_the_line_that_needs_it()
    {
        // Bus 1 = confort (id le plus petit), bus 2 = standard. Le voyage urbain (06:00) passerait en premier en chronologique
        // et prendrait le bus confort ; le voyage interurbain (06:30), qui exige un confort, resterait sans bus.
        var policy = Policy(p =>
        {
            p.CategoryRules.Add(new LineTypeCategoryRule { LineTypeId = 1, AllowedCategoryIds = { 1, 2 } });
            p.CategoryRules.Add(new LineTypeCategoryRule { LineTypeId = 2, AllowedCategoryIds = { 2 } });
            SetMode(p, "BP03", ConstraintMode.Disabled);   // sans critère de non-gaspillage : seul l'ordre compte
        });
        var trips = new[] { Trip(1, 6, 7, lineType: 1), Trip(2, 6.5, 10, line: 13, lineType: 2) };
        var pool = Pool(new[] { Bus(1, category: 2), Bus(2, category: 1) },
            new[] { Agent(1, AgentRole.Driver), Agent(2, AgentRole.Driver), Agent(10, AgentRole.Receiver), Agent(11, AgentRole.Receiver) });
        var allocator = new TripAllocator(policy);

        var greedy = new ChronologicalConstructionStrategy().Build(trips, pool, new ScheduleBook(), allocator, NoLabels);
        var phased = new PhasedConstructionStrategy().Build(trips, pool, new ScheduleBook(), allocator, NoLabels);

        Assert.Equal(1, greedy.State.UnassignedCount);
        Assert.Equal(0, phased.State.UnassignedCount);
        Assert.Equal(2, phased.Phases.Count);
        Assert.Equal(2L, phased.Phases[0].LineTypeId);   // le type le plus tendu est traité en premier
    }

    [Fact]
    public void Alns_never_degrades_the_plan_and_result_is_valid()
    {
        var (trips, pool) = RandomScenario(50, 3);
        var policy = Policy(p => { p.Alns.MaxIterations = 150; p.Alns.MaxSeconds = 60; p.Alns.Seed = 5; });
        var allocator = new TripAllocator(policy);

        var initial = new PhasedConstructionStrategy().Build(trips, pool, new ScheduleBook(), allocator, NoLabels).State;
        var result = new AlnsImprover().Improve(initial, pool, allocator, null, CancellationToken.None);

        Assert.True(result.FinalObjective <= result.InitialObjective + 1e-9);
        Assert.True(result.Best.UnassignedCount <= initial.UnassignedCount);
        Assert.Empty(PlanValidator.Validate(result.Best, pool, allocator));
    }

    [Fact]
    public void Alns_is_reproducible_with_the_same_seed()
    {
        var (trips, pool) = RandomScenario(40, 11);
        var policy = Policy(p => { p.Alns.MaxIterations = 100; p.Alns.MaxSeconds = 60; p.Alns.Seed = 9; });
        var allocator = new TripAllocator(policy);

        string Run()
        {
            var initial = new PhasedConstructionStrategy().Build(trips, pool, new ScheduleBook(), allocator, NoLabels).State;
            var best = new AlnsImprover().Improve(initial, pool, allocator, null, CancellationToken.None).Best;
            return string.Join(";", best.Assignments.Values.OrderBy(a => a.Trip.Id).Select(a => $"{a.Trip.Id}:{a.Bus.Id}/{a.Driver.Id}/{a.Receiver.Id}"));
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Alns_stops_when_cancellation_is_requested_and_still_returns_a_plan()
    {
        var (trips, pool) = RandomScenario(30, 1);
        var allocator = new TripAllocator(Policy());
        var initial = new PhasedConstructionStrategy().Build(trips, pool, new ScheduleBook(), allocator, NoLabels).State;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = new AlnsImprover().Improve(initial, pool, allocator, null, cancellation.Token);
        Assert.Equal(0, result.Iterations);
        Assert.Equal(initial.Assignments.Count, result.Best.Assignments.Count);
    }

    [Fact]
    public void Validator_detects_a_plan_that_violates_a_hard_rule()
    {
        var trips = new[] { Trip(1, 6, 8), Trip(2, 7, 9) };
        var bus = Bus(1);
        var driver1 = Agent(1, AgentRole.Driver); var driver2 = Agent(2, AgentRole.Driver);
        var receiver1 = Agent(10, AgentRole.Receiver); var receiver2 = Agent(11, AgentRole.Receiver);
        var pool = Pool(new[] { bus }, new[] { driver1, driver2, receiver1, receiver2 });
        var allocator = new TripAllocator(Policy());

        // Plan truqué à la main : le même bus sur deux voyages qui se chevauchent.
        var state = new AllocationState(trips, new ScheduleBook());
        state.Assign(new TripAssignment(trips[0], bus, driver1, receiver1, 0, 0, ""));
        state.Assign(new TripAssignment(trips[1], bus, driver2, receiver2, 0, 0, ""));

        var violations = PlanValidator.Validate(state, pool, allocator);
        Assert.True(violations.Any(v => v.Code == "B03"));
    }

    [Fact]
    public void Final_scores_are_recomputed_exactly_and_explained()
    {
        var (trips, pool) = RandomScenario(10, 2);
        var allocator = new TripAllocator(Policy());
        var state = new PhasedConstructionStrategy().Build(trips, pool, new ScheduleBook(), allocator, NoLabels).State;

        var final = PlanEvaluator.BuildFinalAssignments(state, pool, allocator);
        Assert.Equal(state.Assignments.Count, final.Count);
        Assert.True(final.All(a => a.Justification.Contains("Bus #") && a.Score <= a.MaxScore + 1e-9));
    }

    [Fact]
    public void Feasibility_analysis_flags_a_bus_shortage()
    {
        var trips = new[] { Trip(1, 6, 8), Trip(2, 6, 8), Trip(3, 6, 8) };   // 3 voyages simultanés
        var pool = Pool(new[] { Bus(1), Bus(2) }, new[] { Agent(1, AgentRole.Driver), Agent(10, AgentRole.Receiver) });

        var lines = FeasibilityAnalyzer.Analyze(trips, pool, Policy(), new DateOnly(2026, 10, 8), NoLabels);
        var busLine = lines.First(l => l.Scope.StartsWith("Bus (tous"));
        Assert.Equal(3, busLine.Demand);
        Assert.Equal(2, busLine.Supply);
        Assert.False(busLine.IsSufficient);
    }
}
