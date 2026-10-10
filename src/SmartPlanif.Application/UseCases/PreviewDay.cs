using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Allocation;

namespace SmartPlanif.Application.UseCases;

/// <summary>Nombre de voyages d'un type de ligne.</summary>
public sealed record LineTypeSummary(string Label, int TripCount);

/// <summary>Aperçu d'une journée avant allocation : volumes et diagnostic de faisabilité (phase 0).</summary>
public sealed record DayPreview(
    DateOnly Date, int TripsToAllocate, int LockedTrips, int ActiveBuses, int Drivers, int Receivers,
    IReadOnlyList<LineTypeSummary> LineTypes, IReadOnlyList<FeasibilityLine> Feasibility);

/// <summary>Cas d'usage « analyser la journée » : ne modifie rien, signale les pénuries de ressources.</summary>
public sealed class PreviewDayHandler(
    ITripRepository trips, IResourceRepository resources, IReferenceDataRepository reference, IAllocationSettingsStore settings)
{
    /// <summary>Calcule l'aperçu de la journée avec la politique enregistrée (ou celle fournie).</summary>
    public async Task<DayPreview> HandleAsync(DateOnly date, AllocationPolicy? policyOverride, CancellationToken ct)
    {
        var policy = policyOverride ?? await settings.LoadAsync(ct);
        var toAllocate = await trips.GetToAllocateAsync(date, ct);
        var locked = await trips.GetAssignedAsync(date.AddDays(-1), date.AddDays(1), ct);
        var pool = await resources.GetPoolAsync(date.AddDays(-1), date.AddDays(1), ct);
        var labels = (await reference.GetLineTypesAsync(ct)).ToDictionary(x => x.Id, x => x.Label);

        var feasibility = FeasibilityAnalyzer.Analyze(toAllocate, pool, policy, date, labels);
        var lineTypes = toAllocate
            .GroupBy(t => t.LineTypeId)
            .OrderBy(g => g.Key)
            .Select(g => new LineTypeSummary(
                g.Key is { } id ? labels.GetValueOrDefault(id, $"Type {id}") : "Type inconnu", g.Count()))
            .ToList();

        return new DayPreview(
            date, toAllocate.Count, locked.Count,
            pool.Buses.Count(b => b.Active),
            pool.Agents.Count(a => a.Role == Domain.Entities.AgentRole.Driver),
            pool.Agents.Count(a => a.Role == Domain.Entities.AgentRole.Receiver),
            lineTypes, feasibility);
    }
}
