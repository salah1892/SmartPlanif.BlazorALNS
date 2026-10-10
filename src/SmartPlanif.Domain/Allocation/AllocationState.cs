using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Allocation;

/// <summary>
/// État d'un plan en cours de construction : affectations réalisées + plannings des ressources.
/// C'est l'objet que la métaheuristique copie, détruit partiellement puis répare.
/// </summary>
public sealed class AllocationState
{
    private readonly Dictionary<long, TripAssignment> _assignments;

    /// <summary>Crée un état vide pour les voyages à planifier ; <paramref name="book"/> contient déjà les voyages verrouillés.</summary>
    public AllocationState(IReadOnlyList<Trip> tripsToAllocate, ScheduleBook book)
        : this(tripsToAllocate, book, new Dictionary<long, TripAssignment>()) { }

    private AllocationState(IReadOnlyList<Trip> tripsToAllocate, ScheduleBook book, Dictionary<long, TripAssignment> assignments)
    {
        AllTrips = tripsToAllocate;
        Book = book;
        _assignments = assignments;
    }

    /// <summary>Tous les voyages à planifier (affectés ou non).</summary>
    public IReadOnlyList<Trip> AllTrips { get; }

    /// <summary>Plannings de toutes les ressources.</summary>
    public ScheduleBook Book { get; }

    /// <summary>Affectations actuelles, indexées par identifiant de voyage.</summary>
    public IReadOnlyDictionary<long, TripAssignment> Assignments => _assignments;

    /// <summary>Voyages sans affectation.</summary>
    public IEnumerable<Trip> UnassignedTrips => AllTrips.Where(t => !_assignments.ContainsKey(t.Id));

    /// <summary>Nombre de voyages sans affectation.</summary>
    public int UnassignedCount => AllTrips.Count - _assignments.Count;

    /// <summary>Enregistre une affectation et met à jour les trois plannings.</summary>
    public void Assign(TripAssignment assignment)
    {
        if (_assignments.ContainsKey(assignment.Trip.Id))
            throw new InvalidOperationException($"Le voyage {assignment.Trip.Id} est déjà affecté.");
        Book.Commit(assignment.Trip, assignment.Bus.Id, assignment.Driver.Id, assignment.Receiver.Id);
        _assignments[assignment.Trip.Id] = assignment;
    }

    /// <summary>Annule l'affectation d'un voyage et libère ses ressources. Renvoie faux si le voyage n'était pas affecté.</summary>
    public bool Unassign(long tripId)
    {
        if (!_assignments.Remove(tripId, out var assignment)) return false;
        Book.Remove(assignment.Trip, assignment.Bus.Id, assignment.Driver.Id, assignment.Receiver.Id);
        return true;
    }

    /// <summary>Copie profonde et indépendante de l'état.</summary>
    public AllocationState Clone() => new(AllTrips, Book.Clone(), new Dictionary<long, TripAssignment>(_assignments));
}
