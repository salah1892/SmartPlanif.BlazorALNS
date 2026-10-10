using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Rules.Soft;

// Convention commune : 1 = idéal, 0 = pire cas, 0,5 = donnée inconnue (neutre : ni bonus ni pénalité).

/// <summary>Même centre / même délégation que la ligne (bus et agents).</summary>
public sealed class MatchRule<T>(string code, double weight, Func<T, long?> resourceKey, Func<Trip, long?> tripKey) : ISoftRule<T>
{
    /// <inheritdoc />
    public string Code => code;
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<T> c)
    {
        var resourceValue = resourceKey(c.Resource);
        var tripValue = tripKey(c.Trip);
        return resourceValue is null || tripValue is null ? 0.5 : resourceValue == tripValue ? 1 : 0;
    }
}

/// <summary>Peu de voyages déjà effectués dans la journée (équilibrage naturel de la charge).</summary>
public sealed class FewTripsRule<T>(string code, double weight) : ISoftRule<T>
{
    /// <inheritdoc />
    public string Code => code;
    /// <inheritdoc />
    public double Weight => weight;
    /// <inheritdoc />
    public double Evaluate(RuleContext<T> c) => 1.0 / (1 + c.Schedule.TripCountOn(c.Trip.WorkDate));
}

/// <summary>B06 en mode souple : 1 si la catégorie est autorisée pour le type de ligne, sinon 0.</summary>
public sealed class CategoryAllowedSoftRule(double weight) : ISoftRule<Bus>
{
    /// <inheritdoc />
    public string Code => "B06";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Bus> c)
    {
        var rule = c.Policy.FindCategoryRule(c.Trip.LineTypeId);
        if (rule is null || rule.AllowedCategoryIds.Count == 0) return 0.5;
        return c.Resource.CategoryId is { } category && rule.AllowedCategoryIds.Contains(category) ? 1 : 0;
    }
}

/// <summary>BP03 : catégorie préférée pour le type de ligne (1), simplement autorisée (0,5), interdite (0). Évite le gaspillage de bus confort.</summary>
public sealed class CategoryPreferenceSoftRule(double weight) : ISoftRule<Bus>
{
    /// <inheritdoc />
    public string Code => "BP03";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Bus> c)
    {
        var rule = c.Policy.FindCategoryRule(c.Trip.LineTypeId);
        if (rule is null || rule.PreferredCategoryIds.Count == 0) return 0.5;
        if (c.Resource.CategoryId is not { } category) return 0;
        if (rule.PreferredCategoryIds.Contains(category)) return 1;
        return rule.AllowedCategoryIds.Count == 0 || rule.AllowedCategoryIds.Contains(category) ? 0.5 : 0;
    }
}

/// <summary>BP04 : capacité adaptée à la demande prévue (rapport demande / capacité).</summary>
public sealed class CapacityFitRule(double weight) : ISoftRule<Bus>
{
    /// <inheritdoc />
    public string Code => "BP04";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Bus> c) =>
        c.Trip.ExpectedPassengers is { } n && c.Resource.Capacity > 0
            ? Math.Clamp((double)n / c.Resource.Capacity, 0, 1)
            : 0.5;
}

/// <summary>BP05 : le bus repart de la station où son voyage précédent s'est terminé (peu de déplacement à vide).</summary>
public sealed class StationContinuityRule(double weight) : ISoftRule<Bus>
{
    /// <inheritdoc />
    public string Code => "BP05";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Bus> c)
    {
        var previous = c.Schedule.Previous(c.Trip.Departure);
        if (previous is null || previous.DestinationStationId is null || c.Trip.OriginStationId is null) return 0.5;
        return previous.DestinationStationId == c.Trip.OriginStationId ? 1 : 0;
    }
}

/// <summary>BP06 : peu de kilomètres déjà parcourus dans la journée.</summary>
public sealed class LowKmRule(double weight) : ISoftRule<Bus>
{
    /// <inheritdoc />
    public string Code => "BP06";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Bus> c) =>
        1 - Math.Clamp(c.Schedule.KmOn(c.Trip.WorkDate) / c.Policy.MaxBusKmPerDay, 0, 1);
}

/// <summary>BP08 : bus récent (score décroissant avec l'âge, nul à 20 ans).</summary>
public sealed class NewerBusRule(double weight, double maxAgeYears = 20) : ISoftRule<Bus>
{
    /// <inheritdoc />
    public string Code => "BP08";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Bus> c)
    {
        if (c.Resource.InServiceSince is not { } since) return 0.5;
        var ageYears = (c.Trip.Departure - since).TotalDays / 365.25;
        return 1 - Math.Clamp(ageYears / maxAgeYears, 0, 1);
    }
}

/// <summary>AP03 : repos élevé depuis le dernier voyage (saturé à RestSaturation).</summary>
public sealed class RestTimeRule(double weight) : ISoftRule<Agent>
{
    /// <inheritdoc />
    public string Code => "AP03";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Agent> c)
    {
        var previous = c.Schedule.Previous(c.Trip.Departure);
        if (previous is null) return 1;
        return Math.Clamp((c.Trip.Departure - previous.Arrival) / c.Policy.RestSaturation, 0, 1);
    }
}

/// <summary>AP04 : peu d'heures déjà travaillées aujourd'hui.</summary>
public sealed class LowWorkedHoursRule(double weight) : ISoftRule<Agent>
{
    /// <inheritdoc />
    public string Code => "AP04";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Agent> c) =>
        1 - Math.Clamp(c.Schedule.WorkedOn(c.Trip.WorkDate) / c.Policy.MaxDailyWork, 0, 1);
}

/// <summary>AP06 : continuité sur la même ligne que le voyage précédent.</summary>
public sealed class SameLineContinuityRule(double weight) : ISoftRule<Agent>
{
    /// <inheritdoc />
    public string Code => "AP06";
    /// <inheritdoc />
    public double Weight => weight;

    /// <inheritdoc />
    public double Evaluate(RuleContext<Agent> c)
    {
        var previous = c.Schedule.Previous(c.Trip.Departure);
        return previous is null ? 0.5 : previous.LineId == c.Trip.LineId ? 1 : 0;
    }
}
