using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Common;
using SmartPlanif.Domain.Constraints;
using SmartPlanif.Domain.Entities;
using Xunit;
using static SmartPlanif.Domain.Tests.TestData;

namespace SmartPlanif.Domain.Tests;

/// <summary>Règles dures et souples, et effet du choix de contraintes (désactivée / dure / souple).</summary>
public class ConstraintTests
{
    [Fact]
    public void Agent_on_rest_day_is_excluded()
    {
        var result = Run(new[] { Trip(1, 6, 8) }, new[] { Bus(1) },
            new[] { Agent(1, AgentRole.Driver, DayOfWeek.Thursday), Agent(2, AgentRole.Driver), Agent(10, AgentRole.Receiver) });
        Assert.Equal(2, result.Assignments.Single().Driver.Id);
    }

    [Fact]
    public void Inactive_bus_is_excluded()
    {
        var result = Run(new[] { Trip(1, 6, 8) }, new[] { Bus(1, active: false), Bus(2) }, TwoAgents());
        Assert.Equal(2, result.Assignments.Single().Bus.Id);
    }

    [Fact]
    public void Bus_in_maintenance_is_excluded()
    {
        var maintenance = new Unavailability(ResourceKind.Bus, 1, new TimeInterval(Day.AddHours(5), Day.AddHours(9)), "Maintenance");
        var result = Run(new[] { Trip(1, 6, 8) }, new[] { Bus(1), Bus(2) }, TwoAgents(), unavailabilities: new[] { maintenance });
        Assert.Equal(2, result.Assignments.Single().Bus.Id);
    }

    [Fact]
    public void Overlapping_trips_cannot_share_the_only_bus()
    {
        var agents = new[] { Agent(1, AgentRole.Driver), Agent(2, AgentRole.Driver), Agent(10, AgentRole.Receiver), Agent(11, AgentRole.Receiver) };
        var result = Run(new[] { Trip(1, 6, 8), Trip(2, 7, 9) }, new[] { Bus(1) }, agents);

        Assert.Equal(1, result.Assignments.Count);
        var failure = result.Unassigned.Single();
        Assert.Contains("BUS", failure.MissingRoles);
        Assert.True(failure.RejectionsByCode.ContainsKey("B03"));
    }

    [Fact]
    public void Daily_work_limit_is_enforced_by_default()
    {
        // 4 h + 5 h = 9 h > 8 h pour l'unique chauffeur
        var agents = new[] { Agent(1, AgentRole.Driver), Agent(10, AgentRole.Receiver), Agent(11, AgentRole.Receiver) };
        var result = Run(new[] { Trip(1, 6, 10), Trip(2, 11, 16) }, new[] { Bus(1), Bus(2) }, agents);

        var failure = result.Unassigned.Single();
        Assert.Equal(2, failure.Trip.Id);
        Assert.True(failure.RejectionsByCode.ContainsKey("A08"));
    }

    [Fact]
    public void Disabling_a_constraint_removes_its_effect()
    {
        var policy = Policy(p => SetMode(p, "A08", ConstraintMode.Disabled));
        var agents = new[] { Agent(1, AgentRole.Driver), Agent(10, AgentRole.Receiver), Agent(11, AgentRole.Receiver) };
        var result = Run(new[] { Trip(1, 6, 10), Trip(2, 11, 16) }, new[] { Bus(1), Bus(2) }, agents, policy);

        Assert.Empty(result.Unassigned);
    }

    [Fact]
    public void Locked_constraint_cannot_be_disabled()
    {
        var policy = Policy(p => SetMode(p, "B03", ConstraintMode.Disabled));   // tentative de désactiver le chevauchement
        var agents = new[] { Agent(1, AgentRole.Driver), Agent(2, AgentRole.Driver), Agent(10, AgentRole.Receiver), Agent(11, AgentRole.Receiver) };
        var result = Run(new[] { Trip(1, 6, 8), Trip(2, 7, 9) }, new[] { Bus(1) }, agents, policy);

        Assert.Equal(1, result.Unassigned.Count);
    }

    [Fact]
    public void Same_center_as_soft_rule_prefers_but_does_not_exclude()
    {
        var result = Run(new[] { Trip(1, 6, 8, center: 1) },
            new[] { Bus(1, center: 2), Bus(2, center: 1) },
            new[] { Agent(1, AgentRole.Driver, center: 2), Agent(2, AgentRole.Driver, center: 1),
                    Agent(10, AgentRole.Receiver, center: 2), Agent(11, AgentRole.Receiver, center: 1) });
        var a = result.Assignments.Single();
        Assert.Equal((2L, 2L, 11L), (a.Bus.Id, a.Driver.Id, a.Receiver.Id));

        // Avec un seul bus d'un autre centre : accepté (règle souple).
        var other = Run(new[] { Trip(1, 6, 8, center: 1) }, new[] { Bus(1, center: 2) }, TwoAgents());
        Assert.Empty(other.Unassigned);
    }

    [Fact]
    public void Same_center_as_hard_rule_excludes_other_centers()
    {
        var policy = Policy(p => SetMode(p, "B07", ConstraintMode.Hard));
        var result = Run(new[] { Trip(1, 6, 8, center: 1) }, new[] { Bus(1, center: 2) }, TwoAgents(), policy);

        var failure = result.Unassigned.Single();
        Assert.True(failure.RejectionsByCode.ContainsKey("B07"));
    }

    [Fact]
    public void Intercity_line_requires_comfort_bus_when_category_rule_is_hard()
    {
        var policy = Policy(p => p.CategoryRules.Add(new LineTypeCategoryRule { LineTypeId = 2, AllowedCategoryIds = { 2 }, PreferredCategoryIds = { 2 } }));
        var trip = Trip(1, 6, 10, lineType: 2);

        var withComfort = Run(new[] { trip }, new[] { Bus(1, category: 1), Bus(2, category: 2) }, TwoAgents(), policy);
        Assert.Equal(2, withComfort.Assignments.Single().Bus.Id);

        var onlyStandard = Run(new[] { trip }, new[] { Bus(1, category: 1) }, TwoAgents(), policy);
        Assert.True(onlyStandard.Unassigned.Single().RejectionsByCode.ContainsKey("B06"));
    }

    [Fact]
    public void Disabling_category_rule_ignores_the_matrix()
    {
        var policy = Policy(p =>
        {
            p.CategoryRules.Add(new LineTypeCategoryRule { LineTypeId = 2, AllowedCategoryIds = { 2 } });
            SetMode(p, "B06", ConstraintMode.Disabled);
        });
        var result = Run(new[] { Trip(1, 6, 10, lineType: 2) }, new[] { Bus(1, category: 1) }, TwoAgents(), policy);
        Assert.Empty(result.Unassigned);
    }

    [Fact]
    public void Preferred_category_keeps_comfort_buses_for_long_lines()
    {
        var policy = Policy(p => p.CategoryRules.Add(
            new LineTypeCategoryRule { LineTypeId = 1, AllowedCategoryIds = { 1, 2 }, PreferredCategoryIds = { 1 } }));

        // Le bus confort a l'identifiant le plus petit : seul le critère « catégorie préférée » peut faire choisir le standard.
        var result = Run(new[] { Trip(1, 6, 7, lineType: 1) }, new[] { Bus(1, category: 2), Bus(2, category: 1) }, TwoAgents(), policy);
        Assert.Equal(2, result.Assignments.Single().Bus.Id);
    }

    [Fact]
    public void Constraint_catalog_normalizes_invalid_settings()
    {
        var policy = new AllocationPolicy();
        policy.Constraints["A05"] = new ConstraintSetting(ConstraintMode.Disabled, 0);   // verrouillée
        policy.Constraints["A08"] = new ConstraintSetting(ConstraintMode.Soft, 5);        // mode non supporté
        ConstraintCatalog.NormalizeSettings(policy);

        Assert.Equal(ConstraintMode.Hard, policy.Constraints["A05"].Mode);
        Assert.Equal(ConstraintMode.Hard, policy.Constraints["A08"].Mode);
        Assert.Equal(ConstraintCatalog.AllDescriptors.Count, policy.Constraints.Count);
    }

    [Fact]
    public void Policy_validator_reports_incoherent_values()
    {
        var policy = Policy(p => { p.Alns.CoolingRate = 1.5; p.MaxBusKmPerDay = 0; });
        var errors = PolicyValidator.Validate(policy);
        Assert.True(errors.Count >= 2);
        Assert.Empty(PolicyValidator.Validate(Policy()));
    }
}
