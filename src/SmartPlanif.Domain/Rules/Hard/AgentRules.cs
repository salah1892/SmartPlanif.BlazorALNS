using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Rules.Hard;

public sealed class RestDayRule : IHardRule<Agent>
{
    public string Code => "A03";
    public RuleViolation? Check(RuleContext<Agent> c)
    {
        if (c.Resource.RestDay is not { } rest) return null;
        return c.Trip.Departure.DayOfWeek == rest || c.Trip.Arrival.DayOfWeek == rest
            ? new(Code, "Jour de repos") : null;
    }
}

public sealed class DailyWorkRule : IHardRule<Agent>
{
    public string Code => "A08";
    public RuleViolation? Check(RuleContext<Agent> c) =>
        c.Schedule.WorkedOn(c.Trip.WorkDate) + c.Trip.Interval.Duration > c.Policy.MaxDailyWork
            ? new(Code, "Limite quotidienne d'heures dépassée") : null;
}

public sealed class AmplitudeRule : IHardRule<Agent>
{
    public string Code => "A10";
    public RuleViolation? Check(RuleContext<Agent> c) =>
        c.Schedule.SpanOn(c.Trip.WorkDate, c.Trip.Interval) > c.Policy.MaxAmplitude
            ? new(Code, "Amplitude maximale dépassée") : null;
}

public sealed class MaxTripsPerDayRule : IHardRule<Agent>
{
    public string Code => "A09";
    public RuleViolation? Check(RuleContext<Agent> c) =>
        c.Schedule.TripCountOn(c.Trip.WorkDate) >= c.Policy.MaxTripsPerAgentPerDay
            ? new(Code, "Nombre maximal de voyages atteint") : null;
}
