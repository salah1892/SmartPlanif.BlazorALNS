namespace SmartPlanif.Domain.Constraints;

/// <summary>Traitement d'une contrainte : ignorée, éliminatoire (dure) ou pondérée dans le score (souple).</summary>
public enum ConstraintMode { Disabled, Hard, Soft }

/// <summary>Ressource sur laquelle porte la contrainte.</summary>
public enum ConstraintTarget { Bus, Agent }

/// <summary>Choix de l'utilisateur pour une contrainte : mode et poids (le poids ne sert qu'en mode souple).</summary>
public sealed class ConstraintSetting
{
    public ConstraintMode Mode { get; set; }
    public double Weight { get; set; }

    public ConstraintSetting() { }
    public ConstraintSetting(ConstraintMode mode, double weight)
    {
        Mode = mode;
        Weight = weight;
    }
}

/// <summary>Fiche descriptive d'une contrainte, affichée dans l'interface de configuration.</summary>
/// <param name="Code">Code du document de conception (A08, B06...).</param>
/// <param name="Name">Nom court.</param>
/// <param name="Description">Explication métier.</param>
/// <param name="Target">Bus ou agent.</param>
/// <param name="SupportedModes">Modes que l'utilisateur peut choisir.</param>
/// <param name="DefaultMode">Mode par défaut.</param>
/// <param name="DefaultWeight">Poids par défaut (mode souple).</param>
/// <param name="IsLocked">Contrainte non modifiable (impossibilité physique, ex. chevauchement).</param>
public sealed record ConstraintDescriptor(
    string Code, string Name, string Description, ConstraintTarget Target,
    IReadOnlyList<ConstraintMode> SupportedModes, ConstraintMode DefaultMode,
    double DefaultWeight, bool IsLocked);
