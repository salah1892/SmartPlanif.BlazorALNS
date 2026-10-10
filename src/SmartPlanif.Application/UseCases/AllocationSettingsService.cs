using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Allocation;

namespace SmartPlanif.Application.UseCases;

/// <summary>Résultat d'une sauvegarde de politique : succès ou liste d'erreurs de cohérence.</summary>
public sealed record SettingsSaveResult(bool Success, IReadOnlyList<string> Errors);

/// <summary>Cas d'usage « configurer l'allocation » : lecture, validation puis sauvegarde de la politique.</summary>
public sealed class AllocationSettingsService(IAllocationSettingsStore store)
{
    /// <summary>Charge la politique courante.</summary>
    public Task<AllocationPolicy> LoadAsync(CancellationToken ct = default) => store.LoadAsync(ct);

    /// <summary>Valide puis enregistre. Une politique incohérente n'est jamais écrite.</summary>
    public async Task<SettingsSaveResult> SaveAsync(AllocationPolicy policy, CancellationToken ct = default)
    {
        var errors = PolicyValidator.Validate(policy);
        if (errors.Count > 0) return new SettingsSaveResult(false, errors);

        await store.SaveAsync(policy, ct);
        return new SettingsSaveResult(true, errors);
    }

    /// <summary>Restaure la politique par défaut.</summary>
    public Task ResetAsync(CancellationToken ct = default) => store.ResetAsync(ct);
}
