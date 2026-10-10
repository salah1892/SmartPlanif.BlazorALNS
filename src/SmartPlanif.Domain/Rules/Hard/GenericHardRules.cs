using SmartPlanif.Domain.Allocation;

namespace SmartPlanif.Domain.Rules.Hard;

/// <summary>A05 / B03 / B04 : chevauchement avec un voyage déjà planifié.</summary>
public sealed class NoOverlapRule<T>(string code) : IHardRule<T>
{
    public string Code => code;
    public RuleViolation? Check(RuleContext<T> c) =>
        c.Schedule.Overlaps(c.Trip.Interval) ? new(code, "Déjà affecté à un voyage qui chevauche") : null;
}

/// <summary>A04 / B10 : congé, absence, maintenance.</summary>
public sealed class UnavailabilityRule<T>(string code) : IHardRule<T>
{
    public string Code => code;
    public RuleViolation? Check(RuleContext<T> c) =>
        c.Unavailabilities.FirstOrDefault(u => u.Period.Overlaps(c.Trip.Interval)) is { } u
            ? new(code, $"Indisponible : {u.Reason}") : null;
}

/// <summary>A07 / B09 : temps minimal avant ET après le voyage (important quand des voyages sont déjà verrouillés).</summary>
public sealed class MinGapRule<T>(string code, Func<AllocationPolicy, TimeSpan> minGap) : IHardRule<T>
{
    public string Code => code;
    public RuleViolation? Check(RuleContext<T> c)
    {
        var min = minGap(c.Policy);
        var prev = c.Schedule.Previous(c.Trip.Departure);
        if (prev is not null && c.Trip.Departure - prev.Arrival < min)
            return new(code, $"Intervalle avant le voyage < {min.TotalMinutes:0} min");
        var next = c.Schedule.Next(c.Trip.Arrival);
        if (next is not null && next.Departure - c.Trip.Arrival < min)
            return new(code, $"Intervalle après le voyage < {min.TotalMinutes:0} min");
        return null;
    }
}
