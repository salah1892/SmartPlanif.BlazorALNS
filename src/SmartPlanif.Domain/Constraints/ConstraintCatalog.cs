using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Rules;
using SmartPlanif.Domain.Rules.Hard;
using SmartPlanif.Domain.Rules.Soft;

namespace SmartPlanif.Domain.Constraints;

/// <summary>Association d'une fiche descriptive et des fabriques de règles (dure et/ou souple) correspondantes.</summary>
internal sealed record ConstraintDefinition<T>(
    ConstraintDescriptor Descriptor,
    Func<IHardRule<T>>? HardFactory,
    Func<double, ISoftRule<T>>? SoftFactory);

/// <summary>
/// Catalogue de TOUTES les contraintes connues du système. C'est le point central pour ajouter, retirer ou
/// reclasser une contrainte : l'interface Blazor et le moteur d'allocation s'appuient tous deux sur cette liste.
/// L'ordre de déclaration est l'ordre d'évaluation des règles dures : les plus économiques en premier.
/// </summary>
public static class ConstraintCatalog
{
    private static readonly IReadOnlyList<ConstraintDefinition<Bus>> BusDefinitions = CreateBusDefinitions();
    private static readonly IReadOnlyList<ConstraintDefinition<Agent>> AgentDefinitions = CreateAgentDefinitions();
    private static readonly IReadOnlyList<ConstraintDescriptor> AllDescriptorList =
        BusDefinitions.Select(d => d.Descriptor).Concat(AgentDefinitions.Select(d => d.Descriptor)).ToList();

    /// <summary>Fiches de toutes les contraintes (bus puis agents).</summary>
    public static IReadOnlyList<ConstraintDescriptor> AllDescriptors => AllDescriptorList;

    /// <summary>Retrouve la fiche d'une contrainte à partir de son code.</summary>
    public static ConstraintDescriptor? FindDescriptor(string code) =>
        AllDescriptorList.FirstOrDefault(d => d.Code == code);

    /// <summary>Libellé lisible d'un code (« A08 — Heures maximales par jour »), pour les rapports.</summary>
    public static string DescribeCode(string code) =>
        FindDescriptor(code) is { } d ? $"{d.Code} — {d.Name}" : code;

    /// <summary>Choix effectif pour une contrainte : valeur configurée si valide, sinon valeur par défaut.</summary>
    public static ConstraintSetting ResolveSetting(AllocationPolicy policy, ConstraintDescriptor descriptor)
    {
        if (descriptor.IsLocked) return new ConstraintSetting(descriptor.DefaultMode, descriptor.DefaultWeight);

        if (policy.Constraints.TryGetValue(descriptor.Code, out var configured)
            && descriptor.SupportedModes.Contains(configured.Mode))
            return new ConstraintSetting(configured.Mode, Math.Max(0, configured.Weight));

        return new ConstraintSetting(descriptor.DefaultMode, descriptor.DefaultWeight);
    }

    /// <summary>Complète et corrige les choix de la politique : une entrée valide pour chaque contrainte connue.</summary>
    public static void NormalizeSettings(AllocationPolicy policy)
    {
        var normalized = new Dictionary<string, ConstraintSetting>();
        foreach (var descriptor in AllDescriptorList)
            normalized[descriptor.Code] = ResolveSetting(policy, descriptor);
        policy.Constraints = normalized;
    }

    /// <summary>Construit les règles dures et souples applicables aux bus, selon les choix de la politique.</summary>
    public static RuleSet<Bus> BuildBusRuleSet(AllocationPolicy policy) => BuildRuleSet(BusDefinitions, policy);

    /// <summary>Construit les règles dures et souples applicables aux agents, selon les choix de la politique.</summary>
    public static RuleSet<Agent> BuildAgentRuleSet(AllocationPolicy policy) => BuildRuleSet(AgentDefinitions, policy);

    private static RuleSet<T> BuildRuleSet<T>(IEnumerable<ConstraintDefinition<T>> definitions, AllocationPolicy policy)
    {
        var hard = new List<IHardRule<T>>();
        var soft = new List<ISoftRule<T>>();

        foreach (var definition in definitions)
        {
            var setting = ResolveSetting(policy, definition.Descriptor);
            switch (setting.Mode)
            {
                case ConstraintMode.Hard when definition.HardFactory is not null:
                    hard.Add(definition.HardFactory());
                    break;
                case ConstraintMode.Soft when definition.SoftFactory is not null && setting.Weight > 0:
                    soft.Add(definition.SoftFactory(setting.Weight));
                    break;
            }
        }
        return new RuleSet<T>(hard, soft);
    }

    // ------------------------------------------------------------------------------------------
    //  Fabriques de définitions (une par « profil » de contrainte)
    // ------------------------------------------------------------------------------------------

    /// <summary>Contrainte dure non modifiable (impossibilité physique).</summary>
    private static ConstraintDefinition<T> LockedHard<T>(
        string code, string name, string description, ConstraintTarget target, Func<IHardRule<T>> hard) =>
        new(new ConstraintDescriptor(code, name, description, target,
                new[] { ConstraintMode.Hard }, ConstraintMode.Hard, 0, true), hard, null);

    /// <summary>Contrainte dure que l'on peut désactiver.</summary>
    private static ConstraintDefinition<T> HardToggle<T>(
        string code, string name, string description, ConstraintTarget target, Func<IHardRule<T>> hard) =>
        new(new ConstraintDescriptor(code, name, description, target,
                new[] { ConstraintMode.Disabled, ConstraintMode.Hard }, ConstraintMode.Hard, 0, false), hard, null);

    /// <summary>Contrainte qui peut être désactivée, dure ou souple (ex. même centre).</summary>
    private static ConstraintDefinition<T> HardOrSoft<T>(
        string code, string name, string description, ConstraintTarget target,
        ConstraintMode defaultMode, double defaultWeight,
        Func<IHardRule<T>> hard, Func<double, ISoftRule<T>> soft) =>
        new(new ConstraintDescriptor(code, name, description, target,
                new[] { ConstraintMode.Disabled, ConstraintMode.Hard, ConstraintMode.Soft },
                defaultMode, defaultWeight, false), hard, soft);

    /// <summary>Critère de préférence pur (jamais éliminatoire).</summary>
    private static ConstraintDefinition<T> SoftToggle<T>(
        string code, string name, string description, ConstraintTarget target,
        double defaultWeight, Func<double, ISoftRule<T>> soft) =>
        new(new ConstraintDescriptor(code, name, description, target,
                new[] { ConstraintMode.Disabled, ConstraintMode.Soft }, ConstraintMode.Soft, defaultWeight, false), null, soft);

    // ------------------------------------------------------------------------------------------
    //  Contraintes des bus
    // ------------------------------------------------------------------------------------------
    private static IReadOnlyList<ConstraintDefinition<Bus>> CreateBusDefinitions() => new[]
    {
        LockedHard<Bus>("B02", "Bus actif", "Un bus inactif (drvehic.DEACTIF = 0) ne peut jamais être affecté.",
            ConstraintTarget.Bus, () => new BusActiveRule()),
        LockedHard<Bus>("B03", "Pas de chevauchement (bus)", "Un bus ne peut pas effectuer deux voyages qui se chevauchent.",
            ConstraintTarget.Bus, () => new NoOverlapRule<Bus>("B03")),
        HardToggle<Bus>("B10", "Maintenance / indisponibilité", "Exclut un bus en maintenance ou indisponible sur la période du voyage.",
            ConstraintTarget.Bus, () => new UnavailabilityRule<Bus>("B10")),
        HardToggle<Bus>("B09", "Remise en service", "Temps minimal entre deux voyages d'un même bus (paramètre MinBusTurnaround).",
            ConstraintTarget.Bus, () => new MinGapRule<Bus>("B09", p => p.MinBusTurnaround)),
        HardToggle<Bus>("B05", "Capacité suffisante", "La capacité (places assises + debout) doit couvrir la demande prévue, lorsqu'elle est connue.",
            ConstraintTarget.Bus, () => new BusCapacityRule()),
        HardToggle<Bus>("B11", "Kilomètres quotidiens maximum", "Limite le kilométrage d'un bus sur une journée (paramètre MaxBusKmPerDay).",
            ConstraintTarget.Bus, () => new BusDailyKmRule()),
        HardOrSoft<Bus>("B06", "Catégorie compatible avec le type de ligne",
            "Ex. : bus confort obligatoire sur les lignes entre gouvernorats. Règle définie dans la page « Types de ligne ».",
            ConstraintTarget.Bus, ConstraintMode.Hard, 25,
            () => new CategoryCompatibilityRule(), w => new CategoryAllowedSoftRule(w)),
        HardOrSoft<Bus>("B07", "Même centre que la ligne", "Le centre du bus doit être celui de la ligne (dur) ou est préféré (souple).",
            ConstraintTarget.Bus, ConstraintMode.Soft, 40,
            () => new SameValueHardRule<Bus>("B07", "Centre du bus différent de celui de la ligne", b => b.CenterId, t => t.CenterId),
            w => new MatchRule<Bus>("B07", w, b => b.CenterId, t => t.CenterId)),
        HardOrSoft<Bus>("B08", "Même délégation que la ligne", "La délégation du bus doit être celle de la ligne (dur) ou est préférée (souple).",
            ConstraintTarget.Bus, ConstraintMode.Soft, 25,
            () => new SameValueHardRule<Bus>("B08", "Délégation du bus différente de celle de la ligne", b => b.DelegationId, t => t.DelegationId),
            w => new MatchRule<Bus>("B08", w, b => b.DelegationId, t => t.DelegationId)),
        SoftToggle<Bus>("BP03", "Catégorie préférée (non-gaspillage)", "Favorise la catégorie la plus adaptée au type de ligne : garde les bus confort pour les lignes longues.",
            ConstraintTarget.Bus, 25, w => new CategoryPreferenceSoftRule(w)),
        SoftToggle<Bus>("BP04", "Capacité adaptée", "Favorise le bus dont la capacité est proche de la demande prévue.",
            ConstraintTarget.Bus, 20, w => new CapacityFitRule(w)),
        SoftToggle<Bus>("BP05", "Continuité de station", "Favorise le bus qui termine son voyage précédent à la station de départ (moins de déplacement à vide).",
            ConstraintTarget.Bus, 20, w => new StationContinuityRule(w)),
        SoftToggle<Bus>("BP06", "Peu de kilomètres", "Favorise le bus le moins utilisé en kilomètres dans la journée.",
            ConstraintTarget.Bus, 15, w => new LowKmRule(w)),
        SoftToggle<Bus>("BP07", "Peu de voyages (bus)", "Favorise le bus ayant effectué le moins de voyages dans la journée.",
            ConstraintTarget.Bus, 15, w => new FewTripsRule<Bus>("BP07", w)),
        SoftToggle<Bus>("BP08", "Bus récent", "Favorise les bus récents (date de mise en circulation).",
            ConstraintTarget.Bus, 10, w => new NewerBusRule(w)),
    };

    // ------------------------------------------------------------------------------------------
    //  Contraintes des agents (chauffeurs et receveurs)
    // ------------------------------------------------------------------------------------------
    private static IReadOnlyList<ConstraintDefinition<Agent>> CreateAgentDefinitions() => new[]
    {
        LockedHard<Agent>("A05", "Pas de chevauchement (agent)",
            "Un agent ne peut pas effectuer deux voyages simultanés ; garantit aussi qu'il n'est jamais chauffeur et receveur en même temps.",
            ConstraintTarget.Agent, () => new NoOverlapRule<Agent>("A05")),
        HardToggle<Agent>("A03", "Jour de repos", "Exclut l'agent le jour de repos hebdomadaire (dragent.DECJOUR).",
            ConstraintTarget.Agent, () => new RestDayRule()),
        HardToggle<Agent>("A04", "Congés / absences", "Exclut l'agent pendant un congé, une absence ou une suspension.",
            ConstraintTarget.Agent, () => new UnavailabilityRule<Agent>("A04")),
        HardToggle<Agent>("A07", "Repos minimal entre voyages", "Temps de repos minimal entre deux voyages d'un agent (paramètre MinAgentRest).",
            ConstraintTarget.Agent, () => new MinGapRule<Agent>("A07", p => p.MinAgentRest)),
        HardToggle<Agent>("A08", "Heures maximales par jour", "Limite quotidienne de travail (paramètre MaxDailyWork, 8 h par défaut).",
            ConstraintTarget.Agent, () => new DailyWorkRule()),
        HardToggle<Agent>("A09", "Nombre maximal de voyages par jour", "Limite le nombre de voyages d'un agent sur une journée.",
            ConstraintTarget.Agent, () => new MaxTripsPerDayRule()),
        HardToggle<Agent>("A10", "Amplitude maximale", "Limite l'écart entre le premier départ et la dernière arrivée de la journée.",
            ConstraintTarget.Agent, () => new AmplitudeRule()),
        HardOrSoft<Agent>("A11", "Même centre que la ligne", "Le centre de l'agent doit être celui de la ligne (dur) ou est préféré (souple).",
            ConstraintTarget.Agent, ConstraintMode.Soft, 40,
            () => new SameValueHardRule<Agent>("A11", "Centre de l'agent différent de celui de la ligne", a => a.CenterId, t => t.CenterId),
            w => new MatchRule<Agent>("A11", w, a => a.CenterId, t => t.CenterId)),
        HardOrSoft<Agent>("A12", "Même délégation que la ligne", "La délégation de l'agent doit être celle de la ligne (dur) ou est préférée (souple).",
            ConstraintTarget.Agent, ConstraintMode.Soft, 25,
            () => new SameValueHardRule<Agent>("A12", "Délégation de l'agent différente de celle de la ligne", a => a.DelegationId, t => t.DelegationId),
            w => new MatchRule<Agent>("A12", w, a => a.DelegationId, t => t.DelegationId)),
        SoftToggle<Agent>("AP03", "Repos élevé", "Favorise l'agent qui a le plus de repos depuis son dernier voyage.",
            ConstraintTarget.Agent, 20, w => new RestTimeRule(w)),
        SoftToggle<Agent>("AP04", "Peu d'heures travaillées", "Favorise l'agent le moins chargé en heures dans la journée.",
            ConstraintTarget.Agent, 20, w => new LowWorkedHoursRule(w)),
        SoftToggle<Agent>("AP05", "Peu de voyages (agent)", "Favorise l'agent ayant effectué le moins de voyages dans la journée.",
            ConstraintTarget.Agent, 15, w => new FewTripsRule<Agent>("AP05", w)),
        SoftToggle<Agent>("AP06", "Continuité de ligne", "Favorise l'agent qui vient de travailler sur la même ligne.",
            ConstraintTarget.Agent, 10, w => new SameLineContinuityRule(w)),
    };
}
