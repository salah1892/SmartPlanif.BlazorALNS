using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Constraints;
using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Tests;

/// <summary>Fabriques de données de test (voyages, bus, agents, politiques) pour garder les tests lisibles.</summary>
internal static class TestData
{
    /// <summary>Jeudi 8 octobre 2026.</summary>
    public static readonly DateTime Day = new(2026, 10, 8);

    public static Trip Trip(long id, double fromHour, double toHour, long center = 1, long line = 10, long? lineType = 1) =>
        new(id, line, $"Ligne {line}", lineType, Day.AddHours(fromHour), Day.AddHours(toHour), 50, center, center, 1, 2, null);

    public static Bus Bus(long id, bool active = true, long center = 1, long category = 1) =>
        new(id, $"BUS{id}", active, 40, 40, category, center, center, new DateTime(2022, 1, 1));

    public static Agent Agent(long id, AgentRole role, DayOfWeek? restDay = null, long center = 1) =>
        new(id, $"{role}{id}", role, restDay, center, center);

    /// <summary>Politique par défaut ; <paramref name="configure"/> permet d'ajuster contraintes et paramètres.</summary>
    public static AllocationPolicy Policy(Action<AllocationPolicy>? configure = null)
    {
        var policy = new AllocationPolicy();
        configure?.Invoke(policy);
        ConstraintCatalog.NormalizeSettings(policy);
        return policy;
    }

    public static void SetMode(AllocationPolicy policy, string code, ConstraintMode mode, double weight = 10) =>
        policy.Constraints[code] = new ConstraintSetting(mode, weight);

    public static ResourcePool Pool(IEnumerable<Bus> buses, IEnumerable<Agent> agents, IEnumerable<Unavailability>? unavailabilities = null) =>
        new(buses.ToList(), agents.ToList(), unavailabilities ?? Array.Empty<Unavailability>());

    /// <summary>Allocation gloutonne chronologique complète.</summary>
    public static AllocationResult Run(
        IEnumerable<Trip> trips, IEnumerable<Bus> buses, IEnumerable<Agent> agents,
        AllocationPolicy? policy = null, IEnumerable<Unavailability>? unavailabilities = null) =>
        new TripAllocator(policy ?? Policy()).Allocate(trips, Pool(buses, agents, unavailabilities), new ScheduleBook());

    /// <summary>Jeu standard : un chauffeur et un receveur du centre 1.</summary>
    public static IEnumerable<Agent> TwoAgents() => new[] { Agent(1, AgentRole.Driver), Agent(10, AgentRole.Receiver) };

    public static readonly IReadOnlyDictionary<long, string> NoLabels = new Dictionary<long, string>();
}
