using SmartPlanif.Domain.Rules;

namespace SmartPlanif.Domain.Allocation;

/// <summary>Met en forme la justification d'un choix : points obtenus critère par critère.</summary>
public static class JustificationFormatter
{
    /// <summary>Exemple : « Bus #3 : 87,5 pts [B07=40, BP04=10] ».</summary>
    public static string Explain(string label, long resourceId, double score, IEnumerable<ScoreDetail> details)
    {
        var parts = details.Where(d => d.Points > 0).Select(d => $"{d.Code}={d.Points:0.#}");
        return $"{label} #{resourceId} : {score:0.#} pts [{string.Join(", ", parts)}]";
    }
}
