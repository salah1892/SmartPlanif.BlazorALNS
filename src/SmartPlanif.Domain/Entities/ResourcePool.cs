namespace SmartPlanif.Domain.Entities;

public sealed class ResourcePool
{
    private readonly ILookup<(ResourceKind, long), Unavailability> _unavailabilities;

    public ResourcePool(IReadOnlyList<Bus> buses, IReadOnlyList<Agent> agents, IEnumerable<Unavailability> unavailabilities)
    {
        Buses = buses;
        Agents = agents;
        _unavailabilities = unavailabilities.ToLookup(u => (u.Kind, u.ResourceId));
    }

    public IReadOnlyList<Bus> Buses { get; }
    public IReadOnlyList<Agent> Agents { get; }

    public IReadOnlyList<Unavailability> UnavailabilityFor(ResourceKind kind, long id) => _unavailabilities[(kind, id)].ToList();
}
