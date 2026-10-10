using System.Diagnostics;
using SmartPlanif.Domain.Allocation;
using SmartPlanif.Domain.Entities;

namespace SmartPlanif.Domain.Alns;

/// <summary>
/// ALNS (Adaptive Large Neighborhood Search) avec acceptation par recuit simulé.
/// À chaque itération : on copie le plan, on en DÉTRUIT une partie (opérateur choisi selon des poids adaptatifs),
/// on le RÉPARE (toujours en respectant les règles dures), puis on accepte ou refuse le résultat selon l'objectif
/// et la température. Seules des solutions faisables sont visitées.
/// </summary>
public sealed class AlnsImprover
{
    // Récompenses de l'adaptation des poids (valeurs classiques de la littérature ALNS).
    private const double RewardNewBest = 33;
    private const double RewardImproved = 9;
    private const double RewardAccepted = 3;
    private const double MinimumOperatorWeight = 0.1;

    private readonly IReadOnlyList<IDestroyOperator> _destroyOperators = new IDestroyOperator[]
    {
        new RandomRemoval(), new WorstScoreRemoval(), new TimeWindowRemoval(), new ResourceRemoval(),
    };

    private readonly IReadOnlyList<IRepairOperator> _repairOperators = new IRepairOperator[]
    {
        new ChronologicalRepair(), new MostConstrainedFirstRepair(), new RandomOrderRepair(),
    };

    /// <summary>Améliore un plan initial. Le plan fourni n'est jamais modifié.</summary>
    /// <param name="initial">Plan de départ (issu de la construction).</param>
    /// <param name="pool">Ressources disponibles.</param>
    /// <param name="allocator">Allocateur configuré avec les contraintes choisies.</param>
    /// <param name="progress">Réception de la progression (facultatif).</param>
    /// <param name="cancellationToken">Arrêt demandé par l'utilisateur : la recherche rend alors le meilleur plan trouvé.</param>
    public AlnsResult Improve(
        AllocationState initial, ResourcePool pool, TripAllocator allocator,
        IProgress<AlnsProgress>? progress, CancellationToken cancellationToken)
    {
        var settings = allocator.Policy.Alns;
        var random = new Random(settings.Seed);
        var objective = new ObjectiveFunction(allocator.Policy, pool);
        var repairContext = new RepairContext(allocator, pool, random);

        var destroyWeights = Enumerable.Repeat(1.0, _destroyOperators.Count).ToArray();
        var repairWeights = Enumerable.Repeat(1.0, _repairOperators.Count).ToArray();
        var destroyUses = new int[_destroyOperators.Count];
        var repairUses = new int[_repairOperators.Count];
        var destroyRewards = new double[_destroyOperators.Count];
        var repairRewards = new double[_repairOperators.Count];

        var current = initial.Clone();
        var best = initial.Clone();
        var currentValue = objective.Evaluate(current);
        var bestValue = currentValue;
        var initialValue = currentValue;

        var temperature = settings.InitialTemperature;
        var convergence = new List<ConvergencePoint> { new(0, bestValue) };
        var convergenceStep = Math.Max(1, settings.MaxIterations / 200);
        var progressStep = Math.Max(1, settings.MaxIterations / 100);

        var stopwatch = Stopwatch.StartNew();
        var iteration = 0;
        var sinceImprovement = 0;

        while (iteration < settings.MaxIterations
               && stopwatch.Elapsed.TotalSeconds < settings.MaxSeconds
               && sinceImprovement < settings.StagnationLimit
               && !cancellationToken.IsCancellationRequested)
        {
            iteration++;

            var destroyIndex = SelectByRoulette(destroyWeights, random);
            var repairIndex = SelectByRoulette(repairWeights, random);

            // 1. Détruire puis réparer sur une COPIE (le plan courant reste intact en cas de refus).
            var candidate = current.Clone();
            _destroyOperators[destroyIndex].Destroy(candidate, ChooseDestroyCount(candidate, settings, random), random);
            _repairOperators[repairIndex].Repair(candidate, repairContext);
            var candidateValue = objective.Evaluate(candidate);

            // 2. Critère d'acceptation (recuit simulé) et récompense de l'opérateur.
            var reward = 0.0;
            if (candidateValue < bestValue - 1e-9)
            {
                best = candidate.Clone();
                bestValue = candidateValue;
                current = candidate;
                currentValue = candidateValue;
                reward = RewardNewBest;
                sinceImprovement = 0;
            }
            else
            {
                sinceImprovement++;
                if (candidateValue < currentValue - 1e-9)
                {
                    current = candidate;
                    currentValue = candidateValue;
                    reward = RewardImproved;
                }
                else if (random.NextDouble() < Math.Exp(-(candidateValue - currentValue) / Math.Max(temperature, 1e-9)))
                {
                    current = candidate;
                    currentValue = candidateValue;
                    reward = RewardAccepted;
                }
            }

            // 3. Adaptation des poids : un opérateur qui réussit sera tiré plus souvent.
            UpdateWeight(destroyWeights, destroyIndex, reward, settings.ReactionFactor);
            UpdateWeight(repairWeights, repairIndex, reward, settings.ReactionFactor);
            destroyUses[destroyIndex]++; repairUses[repairIndex]++;
            destroyRewards[destroyIndex] += reward; repairRewards[repairIndex] += reward;

            temperature *= settings.CoolingRate;

            if (iteration % convergenceStep == 0) convergence.Add(new ConvergencePoint(iteration, bestValue));
            if (iteration % progressStep == 0)
                progress?.Report(new AlnsProgress(iteration, currentValue, bestValue, best.UnassignedCount, stopwatch.Elapsed, temperature));
        }

        convergence.Add(new ConvergencePoint(iteration, bestValue));
        progress?.Report(new AlnsProgress(iteration, currentValue, bestValue, best.UnassignedCount, stopwatch.Elapsed, temperature));

        var statistics = new List<OperatorStatistic>();
        for (var i = 0; i < _destroyOperators.Count; i++)
            statistics.Add(new OperatorStatistic(_destroyOperators[i].Name, true, destroyUses[i], destroyRewards[i], destroyWeights[i]));
        for (var i = 0; i < _repairOperators.Count; i++)
            statistics.Add(new OperatorStatistic(_repairOperators[i].Name, false, repairUses[i], repairRewards[i], repairWeights[i]));

        return new AlnsResult(best, iteration, stopwatch.Elapsed, initialValue, bestValue, convergence, statistics);
    }

    /// <summary>Nombre d'affectations à détruire : fraction aléatoire des voyages affectés, au moins 2 (ou tous s'il y en a moins).</summary>
    private static int ChooseDestroyCount(AllocationState state, Allocation.AlnsSettings settings, Random random)
    {
        var assigned = state.Assignments.Count;
        if (assigned == 0) return 0;
        var fraction = settings.DestroyFractionMin + random.NextDouble() * (settings.DestroyFractionMax - settings.DestroyFractionMin);
        return Math.Clamp((int)Math.Round(assigned * fraction), Math.Min(2, assigned), assigned);
    }

    /// <summary>Tirage à la roulette : probabilité proportionnelle au poids de l'opérateur.</summary>
    private static int SelectByRoulette(double[] weights, Random random)
    {
        var total = weights.Sum();
        var threshold = random.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (threshold <= cumulative) return i;
        }
        return weights.Length - 1;
    }

    /// <summary>Moyenne mobile exponentielle entre l'ancien poids et la dernière récompense.</summary>
    private static void UpdateWeight(double[] weights, int index, double reward, double reaction) =>
        weights[index] = Math.Max(MinimumOperatorWeight, (1 - reaction) * weights[index] + reaction * reward);
}
