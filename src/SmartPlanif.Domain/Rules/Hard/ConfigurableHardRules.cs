using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Rules.Hard;

/// <summary>
/// Règle dure « même valeur » (centre, délégation...). Si l'une des deux valeurs est inconnue,
/// la ressource n'est pas rejetée (bénéfice du doute : on ne bloque pas sur une donnée absente).
/// </summary>
public sealed class SameValueHardRule<T>(string code, string message, Func<T, long?> resourceKey, Func<Trip, long?> tripKey) : IHardRule<T>
{
    /// <inheritdoc />
    public string Code => code;

    /// <inheritdoc />
    public RuleViolation? Check(RuleContext<T> c)
    {
        var resourceValue = resourceKey(c.Resource);
        var tripValue = tripKey(c.Trip);
        return resourceValue is not null && tripValue is not null && resourceValue != tripValue
            ? new RuleViolation(code, message)
            : null;
    }
}

/// <summary>B06 (dure) : la catégorie du bus doit figurer parmi les catégories autorisées pour le type de ligne.</summary>
public sealed class CategoryCompatibilityRule : IHardRule<Bus>
{
    /// <inheritdoc />
    public string Code => "B06";

    /// <inheritdoc />
    public RuleViolation? Check(RuleContext<Bus> c)
    {
        var rule = c.Policy.FindCategoryRule(c.Trip.LineTypeId);
        if (rule is null || rule.AllowedCategoryIds.Count == 0) return null;   // aucune restriction définie

        return c.Resource.CategoryId is { } category && rule.AllowedCategoryIds.Contains(category)
            ? null
            : new RuleViolation(Code, "Catégorie de bus non autorisée pour ce type de ligne");
    }
}
