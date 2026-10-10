namespace SmartPlanif.Web.Services;

/// <summary>Décrit la source de données active, affichée sur la page d'accueil.</summary>
public sealed record DataSourceInfo(string Description, bool IsDemo);

/// <summary>
/// <see cref="IProgress{T}"/> qui appelle directement le délégué (sans repasser par un contexte de synchronisation),
/// afin que la progression de la métaheuristique soit transmise immédiatement à l'interface.
/// </summary>
public sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    /// <inheritdoc />
    public void Report(T value) => handler(value);
}
