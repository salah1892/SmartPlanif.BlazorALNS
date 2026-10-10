using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Allocation;

/// <summary>
/// Fonction objectif hiérarchique (à MINIMISER) :
/// pénalité x voyages non affectés + déficit de score souple + poids x équité de charge.
/// La pénalité domine tout : un meilleur score n'est jamais obtenu en sacrifiant un voyage.
/// Les scores utilisés sont ceux calculés à l'insertion (approximation volontaire pour la vitesse) ;
/// les scores exacts sont recalculés une fois à la fin par <see cref="PlanEvaluator"/>.
/// </summary>
public sealed class ObjectiveFunction
{
    private readonly AllocationPolicy _policy;
    private readonly IReadOnlyList<long> _driverIds;
    private readonly IReadOnlyList<long> _receiverIds;

    /// <summary>Prépare la fonction pour un jeu de ressources donné.</summary>
    public ObjectiveFunction(AllocationPolicy policy, ResourcePool pool)
    {
        _policy = policy;
        _driverIds = pool.Agents.Where(a => a.Role == AgentRole.Driver).Select(a => a.Id).ToList();
        _receiverIds = pool.Agents.Where(a => a.Role == AgentRole.Receiver).Select(a => a.Id).ToList();
    }

    /// <summary>Valeur de l'objectif pour un état (plus petit = meilleur).</summary>
    public double Evaluate(AllocationState state)
    {
        var scoreDeficit = 0.0;
        foreach (var assignment in state.Assignments.Values)
            scoreDeficit += assignment.MaxScore - assignment.Score;

        var fairness = StandardDeviationOfHours(state.Book, _driverIds) + StandardDeviationOfHours(state.Book, _receiverIds);

        return _policy.Alns.UnassignedPenalty * state.UnassignedCount
             + scoreDeficit
             + _policy.Alns.FairnessWeight * fairness;
    }

    /// <summary>Écart-type des heures de travail d'un groupe d'agents : mesure d'inéquité.</summary>
    private static double StandardDeviationOfHours(ScheduleBook book, IReadOnlyList<long> agentIds)
    {
        if (agentIds.Count == 0) return 0;
        var hours = agentIds.Select(id => book.Agent(id).TotalWorked().TotalHours).ToList();
        var mean = hours.Average();
        return Math.Sqrt(hours.Average(h => (h - mean) * (h - mean)));
    }
}
