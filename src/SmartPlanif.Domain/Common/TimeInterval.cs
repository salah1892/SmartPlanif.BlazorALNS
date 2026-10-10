namespace SmartPlanif.Domain.Common;

/// <summary>Intervalle [Start, End[ . Utiliser des DateTime complets (pas des TimeSpan) pour gérer les voyages après minuit.</summary>
public readonly record struct TimeInterval(DateTime Start, DateTime End)
{
    public TimeSpan Duration => End - Start;

    /// <summary>DébutA &lt; FinB ET FinA &gt; DébutB (règle du document, §5.1). Deux voyages bout à bout ne se chevauchent pas.</summary>
    public bool Overlaps(TimeInterval other) => Start < other.End && End > other.Start;
}
