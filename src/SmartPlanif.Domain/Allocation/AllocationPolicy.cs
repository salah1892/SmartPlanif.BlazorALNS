using SmartPlanif.Domain.Constraints;

namespace SmartPlanif.Domain.Allocation;

/// <summary>
/// Politique d'allocation complète : paramètres métier, choix des contraintes (désactivée / dure / souple),
/// compatibilité type de ligne -> catégorie de bus, réglages de la métaheuristique.
/// Sérialisée en JSON par l'infrastructure ; modifiable depuis l'interface Blazor.
/// </summary>
public sealed class AllocationPolicy
{
    // ---------- Paramètres métier (à valider avec la société de transport) ----------

    /// <summary>Durée de travail maximale d'un agent par jour (règle A08).</summary>
    public TimeSpan MaxDailyWork { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Amplitude maximale : premier départ -> dernière arrivée (règle A10).</summary>
    public TimeSpan MaxAmplitude { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Repos minimal d'un agent entre deux voyages (règle A07).</summary>
    public TimeSpan MinAgentRest { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Temps minimal de remise en service d'un bus entre deux voyages (règle B09).</summary>
    public TimeSpan MinBusTurnaround { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Nombre maximal de voyages par agent et par jour (règle A09).</summary>
    public int MaxTripsPerAgentPerDay { get; set; } = 6;

    /// <summary>Kilométrage quotidien maximal d'un bus (règle B11).</summary>
    public double MaxBusKmPerDay { get; set; } = 500;

    /// <summary>Au-delà de cette durée de repos, le critère « repos » (AP03) est au maximum.</summary>
    public TimeSpan RestSaturation { get; set; } = TimeSpan.FromHours(4);

    // ---------- Choix des contraintes ----------

    /// <summary>Mode (désactivée / dure / souple) et poids de chaque contrainte, indexés par code (ex. « A08 »).</summary>
    public Dictionary<string, ConstraintSetting> Constraints { get; set; } = new();

    /// <summary>Compatibilité entre type de ligne et catégorie de bus.</summary>
    public List<LineTypeCategoryRule> CategoryRules { get; set; } = new();

    // ---------- Métaheuristique ----------

    /// <summary>Réglages de l'ALNS (recherche à grand voisinage adaptative).</summary>
    public AlnsSettings Alns { get; set; } = new();

    /// <summary>Règle de compatibilité d'un type de ligne, ou null s'il n'y a aucune restriction définie.</summary>
    public LineTypeCategoryRule? FindCategoryRule(long? lineTypeId) =>
        lineTypeId is null ? null : CategoryRules.FirstOrDefault(r => r.LineTypeId == lineTypeId);
}

/// <summary>Catégories de bus autorisées et préférées pour un type de ligne.</summary>
public sealed class LineTypeCategoryRule
{
    /// <summary>Type de ligne concerné (drligne.DECTYLI).</summary>
    public long LineTypeId { get; set; }

    /// <summary>Catégories autorisées (drcatve.DECATVH). Liste vide = aucune restriction.</summary>
    public List<long> AllowedCategoryIds { get; set; } = new();

    /// <summary>Catégories préférées (sous-ensemble des autorisées) : bonus de score, évite le gaspillage de bus confort.</summary>
    public List<long> PreferredCategoryIds { get; set; } = new();
}

/// <summary>Réglages de la métaheuristique ALNS avec recuit simulé.</summary>
public sealed class AlnsSettings
{
    /// <summary>Nombre maximal d'itérations.</summary>
    public int MaxIterations { get; set; } = 3000;

    /// <summary>Durée maximale de la recherche, en secondes.</summary>
    public double MaxSeconds { get; set; } = 20;

    /// <summary>Arrêt anticipé après ce nombre d'itérations sans amélioration du meilleur plan.</summary>
    public int StagnationLimit { get; set; } = 1500;

    /// <summary>Part minimale des voyages affectés détruits à chaque itération.</summary>
    public double DestroyFractionMin { get; set; } = 0.05;

    /// <summary>Part maximale des voyages affectés détruits à chaque itération.</summary>
    public double DestroyFractionMax { get; set; } = 0.15;

    /// <summary>Température initiale du recuit simulé (même unité que la fonction objectif).</summary>
    public double InitialTemperature { get; set; } = 100;

    /// <summary>Facteur de refroidissement géométrique, strictement entre 0 et 1.</summary>
    public double CoolingRate { get; set; } = 0.998;

    /// <summary>Graine aléatoire : à graine égale, résultat identique (reproductibilité).</summary>
    public int Seed { get; set; } = 42;

    /// <summary>Bruit d'insertion (fraction du score maximal) pour diversifier la réparation.</summary>
    public double InsertionNoise { get; set; } = 0.10;

    /// <summary>Pénalité par voyage non affecté : doit dominer tous les autres termes de l'objectif.</summary>
    public double UnassignedPenalty { get; set; } = 100000;

    /// <summary>Poids de l'écart-type de la charge de travail des agents (équité).</summary>
    public double FairnessWeight { get; set; } = 10;

    /// <summary>Vitesse d'adaptation des poids des opérateurs (0 à 1).</summary>
    public double ReactionFactor { get; set; } = 0.10;
}
