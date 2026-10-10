using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Entities;
using SmartPlanif.Domain.Scheduling;

namespace SmartPlanif.Domain.Rules;

public sealed record RuleViolation(string Code, string Message);

/// <summary>Contribution d'un critère souple : Points = Weight x Value, Value dans [0,1].</summary>
public sealed record ScoreDetail(string Code, double Weight, double Value)
{
    public double Points => Weight * Value;
}

public sealed record RuleContext<T>(
    T Resource, Trip Trip, ResourceSchedule Schedule,
    IReadOnlyList<Unavailability> Unavailabilities, AllocationPolicy Policy);

/// <summary>Règle DURE : renvoie une violation => la ressource est éliminée (aucun score ne peut compenser).</summary>
public interface IHardRule<T>
{
    string Code { get; }
    RuleViolation? Check(RuleContext<T> context);
}

/// <summary>Règle SOUPLE : renvoie une valeur normalisée dans [0,1], multipliée ensuite par le poids.</summary>
public interface ISoftRule<T>
{
    string Code { get; }
    double Weight { get; }
    double Evaluate(RuleContext<T> context);
}

public sealed class RuleSet<T>(IReadOnlyList<IHardRule<T>> hard, IReadOnlyList<ISoftRule<T>> soft)
{
    public IReadOnlyList<IHardRule<T>> Hard { get; } = hard;
    public IReadOnlyList<ISoftRule<T>> Soft { get; } = soft;
    public double MaxScore => Soft.Sum(s => s.Weight);
}
