using SmartPlanif.Domain.Constraints;

namespace SmartPlanif.Domain.Allocation;

/// <summary>Contrôle de cohérence d'une politique avant sa sauvegarde ou son utilisation.</summary>
public static class PolicyValidator
{
    /// <summary>Liste des erreurs détectées (vide si la politique est cohérente).</summary>
    public static IReadOnlyList<string> Validate(AllocationPolicy policy)
    {
        var errors = new List<string>();

        if (policy.MaxDailyWork <= TimeSpan.Zero) errors.Add("La durée de travail maximale par jour doit être positive.");
        if (policy.MaxAmplitude < policy.MaxDailyWork) errors.Add("L'amplitude maximale doit être supérieure ou égale à la durée de travail maximale.");
        if (policy.MinAgentRest < TimeSpan.Zero) errors.Add("Le repos minimal d'un agent ne peut pas être négatif.");
        if (policy.MinBusTurnaround < TimeSpan.Zero) errors.Add("Le temps de remise en service d'un bus ne peut pas être négatif.");
        if (policy.MaxTripsPerAgentPerDay < 1) errors.Add("Le nombre maximal de voyages par agent doit être au moins 1.");
        if (policy.MaxBusKmPerDay <= 0) errors.Add("Le kilométrage quotidien maximal d'un bus doit être positif.");
        if (policy.RestSaturation <= TimeSpan.Zero) errors.Add("La durée de saturation du repos doit être positive.");

        foreach (var (code, setting) in policy.Constraints)
        {
            if (setting.Weight < 0) errors.Add($"Le poids de la contrainte {code} ne peut pas être négatif.");
            if (setting.Mode == ConstraintMode.Soft && setting.Weight == 0)
                errors.Add($"La contrainte {code} est souple avec un poids nul : elle n'aurait aucun effet.");
        }

        foreach (var rule in policy.CategoryRules)
        {
            var notAllowed = rule.PreferredCategoryIds.Where(p => rule.AllowedCategoryIds.Count > 0 && !rule.AllowedCategoryIds.Contains(p));
            if (notAllowed.Any())
                errors.Add($"Type de ligne {rule.LineTypeId} : une catégorie préférée doit aussi être autorisée.");
        }

        var alns = policy.Alns;
        if (alns.MaxIterations < 0) errors.Add("ALNS : le nombre d'itérations ne peut pas être négatif.");
        if (alns.MaxSeconds <= 0) errors.Add("ALNS : la durée maximale doit être positive.");
        if (alns.DestroyFractionMin <= 0 || alns.DestroyFractionMax > 1 || alns.DestroyFractionMin > alns.DestroyFractionMax)
            errors.Add("ALNS : la part détruite doit vérifier 0 < min ≤ max ≤ 1.");
        if (alns.InitialTemperature <= 0) errors.Add("ALNS : la température initiale doit être positive.");
        if (alns.CoolingRate <= 0 || alns.CoolingRate >= 1) errors.Add("ALNS : le facteur de refroidissement doit être strictement entre 0 et 1.");
        if (alns.InsertionNoise < 0 || alns.InsertionNoise > 1) errors.Add("ALNS : le bruit d'insertion doit être entre 0 et 1.");
        if (alns.UnassignedPenalty <= 0) errors.Add("ALNS : la pénalité par voyage non affecté doit être positive.");
        if (alns.FairnessWeight < 0) errors.Add("ALNS : le poids de l'équité ne peut pas être négatif.");
        if (alns.ReactionFactor < 0 || alns.ReactionFactor > 1) errors.Add("ALNS : le facteur de réaction doit être entre 0 et 1.");
        if (alns.StagnationLimit < 1) errors.Add("ALNS : la limite de stagnation doit être au moins 1.");

        return errors;
    }
}
