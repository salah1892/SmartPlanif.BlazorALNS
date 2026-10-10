using SmartPlanif.Domain.Common;
using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Scheduling;

/// <summary>Planning en mémoire d'UNE ressource (bus ou agent). Source de vérité pendant l'allocation.</summary>
public sealed class ResourceSchedule
{
    private readonly List<Trip> _trips = new();

    /// <summary>Voyages de la ressource, triés par heure de départ.</summary>
    public IReadOnlyList<Trip> Trips => _trips;

    /// <summary>Ajoute un voyage et conserve l'ordre chronologique.</summary>
    public void Add(Trip trip)
    {
        _trips.Add(trip);
        _trips.Sort((a, b) => a.Departure.CompareTo(b.Departure));
    }

    /// <summary>Retire un voyage (par identifiant). Renvoie vrai si le voyage figurait au planning.</summary>
    public bool Remove(Trip trip) => _trips.RemoveAll(t => t.Id == trip.Id) > 0;

    /// <summary>Copie indépendante du planning (les voyages sont immuables, la copie de liste suffit).</summary>
    public ResourceSchedule Clone()
    {
        var copy = new ResourceSchedule();
        copy._trips.AddRange(_trips);
        return copy;
    }

    /// <summary>Vrai si un voyage déjà planifié chevauche l'intervalle donné.</summary>
    public bool Overlaps(TimeInterval interval) => _trips.Any(t => t.Interval.Overlaps(interval));

    /// <summary>Dernier voyage terminé au plus tard à l'instant donné.</summary>
    public Trip? Previous(DateTime at) => _trips.Where(t => t.Arrival <= at).MaxBy(t => t.Arrival);

    /// <summary>Premier voyage qui démarre au plus tôt à l'instant donné.</summary>
    public Trip? Next(DateTime at) => _trips.Where(t => t.Departure >= at).MinBy(t => t.Departure);

    private IEnumerable<Trip> On(DateOnly day) => _trips.Where(t => t.WorkDate == day);

    /// <summary>Durée de travail cumulée sur un jour.</summary>
    public TimeSpan WorkedOn(DateOnly day) => On(day).Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Interval.Duration);

    /// <summary>Nombre de voyages sur un jour.</summary>
    public int TripCountOn(DateOnly day) => On(day).Count();

    /// <summary>Kilomètres cumulés sur un jour.</summary>
    public double KmOn(DateOnly day) => On(day).Sum(t => t.DistanceKm);

    /// <summary>Durée de travail cumulée sur tout le planning (utilisée pour l'équité de charge).</summary>
    public TimeSpan TotalWorked() => _trips.Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Interval.Duration);

    /// <summary>Amplitude du jour (premier départ -> dernière arrivée), en incluant le voyage candidat.</summary>
    public TimeSpan SpanOn(DateOnly day, TimeInterval candidate)
    {
        var start = candidate.Start;
        var end = candidate.End;
        foreach (var t in On(day))
        {
            if (t.Departure < start) start = t.Departure;
            if (t.Arrival > end) end = t.Arrival;
        }
        return end - start;
    }
}
