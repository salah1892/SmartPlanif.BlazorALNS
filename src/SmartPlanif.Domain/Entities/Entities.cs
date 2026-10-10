using SmartPlanif.Domain.Common;

namespace SmartPlanif.Domain.Entities;

/// <summary>Rôle opérationnel d'un agent (déduit de dragent.DECQUAL via un mapping configurable).</summary>
public enum AgentRole { Driver, Receiver, Other }

/// <summary>Famille de ressource, utilisée pour retrouver les indisponibilités.</summary>
public enum ResourceKind { Bus, Agent }

/// <summary>Agent (chauffeur ou receveur). RestDay est exprimé en DayOfWeek .NET (Sunday = 0).</summary>
public sealed record Agent(
    long Id, string Name, AgentRole Role,
    DayOfWeek? RestDay, long? CenterId, long? DelegationId);

/// <summary>Autobus du parc (table drvehic).</summary>
public sealed record Bus(
    long Id, string Plate, bool Active, int Seats, int Standing,
    long? CategoryId, long? CenterId, long? DelegationId, DateTime? InServiceSince)
{
    /// <summary>Capacité totale = places assises + places debout.</summary>
    public int Capacity => Seats + Standing;
}

/// <summary>
/// Voyage à planifier. L'heure d'arrivée n'existe pas dans la table trips : elle est dérivée de stop_times.
/// LineTypeId provient de drligne.DECTYLI (urbain, interurbain...).
/// </summary>
public sealed record Trip(
    long Id, long LineId, string LineLabel, long? LineTypeId,
    DateTime Departure, DateTime Arrival, double DistanceKm,
    long? CenterId, long? DelegationId,
    long? OriginStationId, long? DestinationStationId,
    int? ExpectedPassengers)
{
    /// <summary>Intervalle d'occupation des ressources.</summary>
    public TimeInterval Interval => new(Departure, Arrival);

    /// <summary>Jour de rattachement (jour du départ) pour les cumuls journaliers.</summary>
    public DateOnly WorkDate => DateOnly.FromDateTime(Departure);
}

/// <summary>Période d'indisponibilité d'une ressource : congé, absence, maintenance...</summary>
public sealed record Unavailability(ResourceKind Kind, long ResourceId, TimeInterval Period, string Reason);

/// <summary>Voyage déjà affecté en dehors de cette exécution : il bloque ses ressources mais n'est pas replanifié.</summary>
public sealed record AssignedTrip(Trip Trip, long BusId, long DriverId, long ReceiverId);
