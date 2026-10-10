using System.Text.Json;
using System.Text.Json.Serialization;
using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Constraints;

namespace SmartPlanif.Infrastructure.Settings;

/// <summary>
/// Stocke la politique d'allocation dans un fichier JSON lisible et modifiable à la main.
/// Un fichier absent ou illisible ne bloque jamais l'application : la politique par défaut est utilisée.
/// </summary>
/// <param name="filePath">Chemin du fichier JSON.</param>
/// <param name="defaultFactory">Fabrique de la politique par défaut (null = valeurs par défaut du code).</param>
public sealed class JsonAllocationSettingsStore(string filePath, Func<AllocationPolicy>? defaultFactory = null) : IAllocationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    /// <inheritdoc />
    public Task<AllocationPolicy> LoadAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            var policy = ReadFromFile() ?? CreateDefault();
            ConstraintCatalog.NormalizeSettings(policy);   // une entrée valide pour chaque contrainte connue
            return Task.FromResult(policy);
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(AllocationPolicy policy, CancellationToken ct)
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Écriture dans un fichier temporaire puis remplacement : pas de fichier à moitié écrit en cas d'incident.
            var temporary = filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(policy, JsonOptions));
            File.Move(temporary, filePath, overwrite: true);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ResetAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        return Task.CompletedTask;
    }

    private AllocationPolicy CreateDefault() => defaultFactory?.Invoke() ?? new AllocationPolicy();

    private AllocationPolicy? ReadFromFile()
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<AllocationPolicy>(File.ReadAllText(filePath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;   // fichier corrompu : on retombe sur les valeurs par défaut
        }
    }
}
