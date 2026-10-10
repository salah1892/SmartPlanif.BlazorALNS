using SmartPlanif.Domain.Constraints;
using SmartPlanif.Infrastructure.Settings;
using Xunit;

namespace SmartPlanif.Domain.Tests;

/// <summary>Persistance JSON de la politique : aller-retour, fichier absent, fichier corrompu.</summary>
public class SettingsStoreTests
{
    private static string TemporaryFile() => Path.Combine(Path.GetTempPath(), $"smartplanif-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Policy_survives_a_save_and_load_round_trip()
    {
        var path = TemporaryFile();
        try
        {
            var store = new JsonAllocationSettingsStore(path);
            var policy = await store.LoadAsync(CancellationToken.None);
            policy.Constraints["B07"] = new ConstraintSetting(ConstraintMode.Hard, 0);
            policy.Constraints["AP04"] = new ConstraintSetting(ConstraintMode.Soft, 33);
            policy.MaxDailyWork = TimeSpan.FromHours(7.5);
            await store.SaveAsync(policy, CancellationToken.None);

            var reloaded = await new JsonAllocationSettingsStore(path).LoadAsync(CancellationToken.None);
            Assert.Equal(ConstraintMode.Hard, reloaded.Constraints["B07"].Mode);
            Assert.Equal(33.0, reloaded.Constraints["AP04"].Weight);
            Assert.Equal(TimeSpan.FromHours(7.5), reloaded.MaxDailyWork);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Corrupted_file_falls_back_to_defaults()
    {
        var path = TemporaryFile();
        try
        {
            await File.WriteAllTextAsync(path, "{ ceci n'est pas du json");
            var policy = await new JsonAllocationSettingsStore(path).LoadAsync(CancellationToken.None);
            Assert.Equal(TimeSpan.FromHours(8), policy.MaxDailyWork);
            Assert.Equal(ConstraintCatalog.AllDescriptors.Count, policy.Constraints.Count);
        }
        finally { File.Delete(path); }
    }
}
