using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Application.Abstractions;

/// <summary>Élément de référence (type de ligne, catégorie de bus...) : identifiant + libellé.</summary>
public sealed record ReferenceItem(long Id, string Label);

/// <summary>Accès aux voyages (tables trips, stop_times, drligne).</summary>
public interface ITripRepository
{
    /// <summary>Voyages valides et planifiables du jour, auxquels il manque au moins une ressource.</summary>
    Task<IReadOnlyList<Trip>> GetToAllocateAsync(DateOnly date, CancellationToken ct);

    /// <summary>Voyages déjà entièrement affectés sur la fenêtre [from, to] : ils bloquent leurs ressources.</summary>
    Task<IReadOnlyList<AssignedTrip>> GetAssignedAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Accès aux ressources (drvehic, dragent) et à leurs indisponibilités.</summary>
public interface IResourceRepository
{
    /// <summary>Bus, agents et indisponibilités couvrant la fenêtre [from, to].</summary>
    Task<ResourcePool> GetPoolAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Enregistrement des propositions d'affectation.</summary>
public interface IAssignmentRepository
{
    /// <summary>Enregistre les affectations avec le statut « PROPOSED » (table trip_assignments).</summary>
    Task SaveProposalsAsync(IReadOnlyList<TripAssignment> assignments, string user, CancellationToken ct);
}

/// <summary>Données de référence servant aux écrans de configuration (tables drtyli, drcatve).</summary>
public interface IReferenceDataRepository
{
    /// <summary>Types de ligne (urbain, interurbain...).</summary>
    Task<IReadOnlyList<ReferenceItem>> GetLineTypesAsync(CancellationToken ct);

    /// <summary>Catégories de véhicules (standard, confort...).</summary>
    Task<IReadOnlyList<ReferenceItem>> GetBusCategoriesAsync(CancellationToken ct);
}

/// <summary>Persistance de la politique d'allocation (choix des contraintes, paramètres, ALNS).</summary>
public interface IAllocationSettingsStore
{
    /// <summary>Charge la politique ; renvoie toujours un objet complet et indépendant (copie).</summary>
    Task<AllocationPolicy> LoadAsync(CancellationToken ct);

    /// <summary>Enregistre la politique.</summary>
    Task SaveAsync(AllocationPolicy policy, CancellationToken ct);

    /// <summary>Revient à la politique par défaut.</summary>
    Task ResetAsync(CancellationToken ct);
}
