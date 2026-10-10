using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Rules;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Allocation;

/// <summary>Ressource candidate avec son planning et ses indisponibilités.</summary>
public sealed record Candidate<T>(long Id, T Resource, ResourceSchedule Schedule, IReadOnlyList<Unavailability> Unavailabilities);

/// <summary>Candidat retenu après filtrage dur : Score est le vrai score, RankingKey sert au tri (score + bruit éventuel).</summary>
public sealed record ScoredCandidate<T>(long Id, T Resource, double Score, IReadOnlyList<ScoreDetail> Details, double RankingKey);

/// <summary>Résultat d'une sélection : candidats classés et nombre de rejets par code de règle dure.</summary>
public sealed record SelectionResult<T>(IReadOnlyList<ScoredCandidate<T>> Candidates, IReadOnlyDictionary<string, int> RejectionsByCode)
{
    /// <summary>Meilleur candidat, ou null si aucun candidat n'est valide.</summary>
    public ScoredCandidate<T>? Best => Candidates.Count > 0 ? Candidates[0] : null;
}

/// <summary>Moteur commun d'application des règles : filtre dur puis score souple, identique pour bus et agents.</summary>
public static class CandidateSelector
{
    /// <summary>Première règle dure violée par la ressource, ou null si elle est éligible.</summary>
    public static RuleViolation? FindFirstViolation<T>(RuleContext<T> context, RuleSet<T> rules)
    {
        foreach (var rule in rules.Hard)
        {
            var violation = rule.Check(context);
            if (violation is not null) return violation;
        }
        return null;
    }

    /// <summary>Détail du score souple : une ligne par critère (poids x valeur normalisée dans [0,1]).</summary>
    public static IReadOnlyList<ScoreDetail> ComputeScoreDetails<T>(RuleContext<T> context, RuleSet<T> rules) =>
        rules.Soft
            .Select(r => new ScoreDetail(r.Code, r.Weight, Math.Clamp(r.Evaluate(context), 0, 1)))
            .ToList();

    /// <summary>Nombre de candidats éligibles (règles dures seulement) : mesure de la difficulté d'un voyage.</summary>
    public static int CountFeasible<T>(IEnumerable<Candidate<T>> candidates, Trip trip, RuleSet<T> rules, AllocationPolicy policy) =>
        candidates.Count(c => FindFirstViolation(
            new RuleContext<T>(c.Resource, trip, c.Schedule, c.Unavailabilities, policy), rules) is null);

    /// <summary>
    /// Filtre par règles dures puis classe par score souple décroissant (puis identifiant croissant : déterminisme).
    /// Si <paramref name="noiseSource"/> est fourni, un bruit proportionnel au score maximal diversifie le classement
    /// (utilisé uniquement par la métaheuristique ; le score affiché reste le vrai score).
    /// </summary>
    public static SelectionResult<T> Select<T>(
        IEnumerable<Candidate<T>> candidates, Trip trip, RuleSet<T> rules, AllocationPolicy policy,
        Random? noiseSource = null, double noiseFraction = 0)
    {
        var accepted = new List<ScoredCandidate<T>>();
        var rejections = new Dictionary<string, int>();
        var noiseAmplitude = noiseFraction * rules.MaxScore;

        foreach (var candidate in candidates)
        {
            var context = new RuleContext<T>(candidate.Resource, trip, candidate.Schedule, candidate.Unavailabilities, policy);

            var violation = FindFirstViolation(context, rules);      // étape 1 : règles dures
            if (violation is not null)
            {
                rejections[violation.Code] = rejections.GetValueOrDefault(violation.Code) + 1;
                continue;
            }

            var details = ComputeScoreDetails(context, rules);       // étape 2 : règles souples
            var score = details.Sum(d => d.Points);
            var noise = noiseSource is null ? 0 : (noiseSource.NextDouble() * 2 - 1) * noiseAmplitude;
            accepted.Add(new ScoredCandidate<T>(candidate.Id, candidate.Resource, score, details, score + noise));
        }

        var sorted = accepted.OrderByDescending(x => x.RankingKey).ThenBy(x => x.Id).ToList();
        return new SelectionResult<T>(sorted, rejections);
    }
}
