using SmartPlanif.Application.Abstractions;
using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Common;
using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Infrastructure.InMemory;

/// <summary>
/// Source de données de démonstration (aucune base requise) : génère de façon déterministe les voyages de n'importe quelle date.
/// Trois types de ligne (urbain, interurbain entre gouvernorats, banlieue) et trois catégories de bus.
/// Les lignes interurbaines n'ont que peu de bus confort : la politique de catégorie et l'ordre d'allocation comptent.
/// </summary>
public sealed class DemoDataStore : ITripRepository, IResourceRepository, IAssignmentRepository, IReferenceDataRepository
{
    private const long Urban = 1, InterCity = 2, Suburban = 3;
    private const long Standard = 1, Comfort = 2, Articulated = 3;

    private static readonly (long Id, string Label)[] LineTypes =
    {
        (Urban, "URBAIN"), (InterCity, "INTERURBAIN (entre gouvernorats)"), (Suburban, "BANLIEUE"),
    };

    private static readonly (long Id, string Label)[] Categories =
    {
        (Standard, "BUS STANDARD"), (Comfort, "BUS CONFORT"), (Articulated, "BUS ARTICULÉ"),
    };

    private static readonly (long LineId, string Label, long TypeId, int DurationMinutes, double Km, int TripCount, int SpacingMinutes)[] Lines =
    {
        (10, "Ligne 10 Centre-Gare", Urban, 75, 22, 5, 170),
        (11, "Ligne 11 Cité Nord", Urban, 60, 18, 5, 170),
        (12, "Ligne 12 Hôpital", Urban, 80, 25, 5, 170),
        (17, "Ligne 17 Université", Urban, 70, 20, 5, 170),
        (13, "Ligne 13 Kairouan-Tunis", InterCity, 210, 160, 4, 240),
        (14, "Ligne 14 Kairouan-Sousse", InterCity, 150, 75, 4, 240),
        (15, "Ligne 15 Banlieue Est", Suburban, 100, 40, 4, 200),
        (16, "Ligne 16 Banlieue Ouest", Suburban, 110, 45, 4, 200),
    };

    /// <summary>Propositions enregistrées pendant la session (consultables pour les tests).</summary>
    public List<TripAssignment> Saved { get; } = new();

    /// <summary>Politique par défaut de la démo : règles de compatibilité type de ligne -> catégorie de bus.</summary>
    public static AllocationPolicy CreateDemoPolicy() => new()
    {
        CategoryRules =
        {
            new LineTypeCategoryRule { LineTypeId = Urban, AllowedCategoryIds = { Standard, Comfort, Articulated }, PreferredCategoryIds = { Standard } },
            new LineTypeCategoryRule { LineTypeId = InterCity, AllowedCategoryIds = { Comfort }, PreferredCategoryIds = { Comfort } },
            new LineTypeCategoryRule { LineTypeId = Suburban, AllowedCategoryIds = { Standard, Comfort }, PreferredCategoryIds = { Standard } },
        },
    };

    // ---------------------------------------------------------------- voyages
    /// <inheritdoc />
    public Task<IReadOnlyList<Trip>> GetToAllocateAsync(DateOnly date, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Trip>>(GenerateTrips(date));

    /// <inheritdoc />
    public Task<IReadOnlyList<AssignedTrip>> GetAssignedAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AssignedTrip>>(Array.Empty<AssignedTrip>());

    private static List<Trip> GenerateTrips(DateOnly date)
    {
        var trips = new List<Trip>();
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        var sequence = 0;

        for (var lineIndex = 0; lineIndex < Lines.Length; lineIndex++)
        {
            var line = Lines[lineIndex];
            var jitter = new Random(date.DayNumber * 31 + (int)line.LineId);   // même date => mêmes voyages
            var centerId = line.LineId % 2 == 0 ? 1L : 2L;

            for (var k = 0; k < line.TripCount; k++)
            {
                var departure = midnight.AddMinutes(330 + lineIndex * 13 + k * line.SpacingMinutes + jitter.Next(0, 40));
                trips.Add(new Trip(
                    Id: date.DayNumber * 1000L + (++sequence),
                    LineId: line.LineId, LineLabel: line.Label, LineTypeId: line.TypeId,
                    Departure: departure, Arrival: departure.AddMinutes(line.DurationMinutes), DistanceKm: line.Km,
                    CenterId: centerId, DelegationId: centerId,
                    OriginStationId: line.LineId * 10 + k % 2, DestinationStationId: line.LineId * 10 + (k + 1) % 2,
                    ExpectedPassengers: null));
            }
        }
        return trips;
    }

    // ---------------------------------------------------------------- ressources
    /// <inheritdoc />
    public Task<ResourcePool> GetPoolAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var buses = new List<Bus>();
        for (var i = 1; i <= 12; i++)
        {
            var category = i <= 6 ? Standard : i <= 9 ? Comfort : Standard;
            if (i is 10 or 11) category = Articulated;
            var center = 1 + i % 2;
            buses.Add(new Bus(i, $"{100 + i} TU {4500 + i}", Active: i != 12, Seats: 40, Standing: 40,
                category, center, center, new DateTime(2014 + i % 9, 1, 1)));
        }

        var agents = new List<Agent>();
        for (var k = 0; k < 14; k++)
        {
            var center = 1 + k % 2;
            agents.Add(new Agent(101 + k, $"Chauffeur {101 + k}", AgentRole.Driver, (DayOfWeek)(k % 7), center, center));
            agents.Add(new Agent(201 + k, $"Receveur {201 + k}", AgentRole.Receiver, (DayOfWeek)((k + 3) % 7), center, center));
        }

        // Indisponibilités du jour central de la fenêtre [J-1, J+1] : maintenance du bus 2, congé du chauffeur 105.
        var day = from.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var unavailabilities = new List<Unavailability>
        {
            new(ResourceKind.Bus, 2, new TimeInterval(day.AddHours(12), day.AddHours(18)), "Maintenance"),
            new(ResourceKind.Agent, 105, new TimeInterval(day, day.AddDays(1)), "Congé"),
        };
        return Task.FromResult(new ResourcePool(buses, agents, unavailabilities));
    }

    // ---------------------------------------------------------------- enregistrement et référentiels
    /// <inheritdoc />
    public Task SaveProposalsAsync(IReadOnlyList<TripAssignment> assignments, string user, CancellationToken ct)
    {
        Saved.AddRange(assignments);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReferenceItem>> GetLineTypesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReferenceItem>>(LineTypes.Select(t => new ReferenceItem(t.Id, t.Label)).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<ReferenceItem>> GetBusCategoriesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReferenceItem>>(Categories.Select(c => new ReferenceItem(c.Id, c.Label)).ToList());
}
