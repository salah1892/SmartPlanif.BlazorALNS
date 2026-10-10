using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Alns;

/// <summary>Contexte transmis aux opérateurs de réparation.</summary>
public sealed record RepairContext(TripAllocator Allocator, ResourcePool Pool, Random Random);

/// <summary>Point de la courbe de convergence (meilleur objectif connu à une itération).</summary>
public sealed record ConvergencePoint(int Iteration, double BestObjective);

/// <summary>Statistiques d'utilisation d'un opérateur, pour analyser l'apport de chacun.</summary>
public sealed record OperatorStatistic(string Name, bool IsDestroy, int Uses, double TotalReward, double FinalWeight);

/// <summary>Information de progression émise pendant la recherche (affichée en direct dans l'interface).</summary>
public sealed record AlnsProgress(int Iteration, double CurrentObjective, double BestObjective, int UnassignedInBest, TimeSpan Elapsed, double Temperature);

/// <summary>Résultat final de la métaheuristique.</summary>
public sealed record AlnsResult(
    AllocationState Best, int Iterations, TimeSpan Elapsed,
    double InitialObjective, double FinalObjective,
    IReadOnlyList<ConvergencePoint> Convergence, IReadOnlyList<OperatorStatistic> OperatorStatistics);

/// <summary>Opérateur de destruction : retire des voyages du plan (libère leurs ressources).</summary>
public interface IDestroyOperator
{
    /// <summary>Nom affiché dans les statistiques.</summary>
    string Name { get; }

    /// <summary>Annule jusqu'à <paramref name="count"/> affectations dans <paramref name="state"/>.</summary>
    void Destroy(AllocationState state, int count, Random random);
}

/// <summary>Opérateur de réparation : réaffecte les voyages non affectés en respectant toujours les règles dures.</summary>
public interface IRepairOperator
{
    /// <summary>Nom affiché dans les statistiques.</summary>
    string Name { get; }

    /// <summary>Tente d'affecter tous les voyages actuellement sans affectation.</summary>
    void Repair(AllocationState state, RepairContext context);
}
