using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Rules.Hard;

public sealed class BusActiveRule : IHardRule<Bus>
{
    public string Code => "B02";
    public RuleViolation? Check(RuleContext<Bus> c) => c.Resource.Active ? null : new(Code, "Bus inactif");
}

/// <summary>B05 : appliquée seulement si le nombre de voyageurs prévu est connu (donnée absente aujourd'hui).</summary>
public sealed class BusCapacityRule : IHardRule<Bus>
{
    public string Code => "B05";
    public RuleViolation? Check(RuleContext<Bus> c) =>
        c.Trip.ExpectedPassengers is { } n && c.Resource.Capacity < n ? new(Code, "Capacité insuffisante") : null;
}

public sealed class BusDailyKmRule : IHardRule<Bus>
{
    public string Code => "B11";
    public RuleViolation? Check(RuleContext<Bus> c) =>
        c.Schedule.KmOn(c.Trip.WorkDate) + c.Trip.DistanceKm > c.Policy.MaxBusKmPerDay
            ? new(Code, "Kilométrage quotidien maximal dépassé") : null;
}
