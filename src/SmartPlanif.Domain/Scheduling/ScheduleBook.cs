using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Scheduling;

/// <summary>
/// Registre des plannings de toutes les ressources. Un agent possède UN SEUL planning,
/// qu'il soit chauffeur ou receveur : la règle « jamais chauffeur ET receveur en même temps » en découle.
/// </summary>
public sealed class ScheduleBook
{
    private readonly Dictionary<long, ResourceSchedule> _buses = new();
    private readonly Dictionary<long, ResourceSchedule> _agents = new();

    /// <summary>Planning d'un bus (créé à la demande).</summary>
    public ResourceSchedule Bus(long id) => _buses.TryGetValue(id, out var s) ? s : _buses[id] = new();

    /// <summary>Planning d'un agent (créé à la demande).</summary>
    public ResourceSchedule Agent(long id) => _agents.TryGetValue(id, out var s) ? s : _agents[id] = new();

    /// <summary>Inscrit un voyage dans les trois plannings concernés.</summary>
    public void Commit(Trip trip, long busId, long driverId, long receiverId)
    {
        Bus(busId).Add(trip);
        Agent(driverId).Add(trip);
        Agent(receiverId).Add(trip);
    }

    /// <summary>Retire un voyage des trois plannings (opération inverse de Commit).</summary>
    public void Remove(Trip trip, long busId, long driverId, long receiverId)
    {
        Bus(busId).Remove(trip);
        Agent(driverId).Remove(trip);
        Agent(receiverId).Remove(trip);
    }

    /// <summary>Copie profonde : indispensable pour tester un mouvement de la métaheuristique sans casser l'état courant.</summary>
    public ScheduleBook Clone()
    {
        var copy = new ScheduleBook();
        foreach (var (id, schedule) in _buses) copy._buses[id] = schedule.Clone();
        foreach (var (id, schedule) in _agents) copy._agents[id] = schedule.Clone();
        return copy;
    }
}
