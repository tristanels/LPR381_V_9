using LPR381.Core.Simplex;

namespace LPR381.Core.Algorithms;

/// <summary>
/// The list of algorithms the program offers, in the order the brief lists them. The menu is built
/// from this list, so adding or finishing an algorithm is a one-line change here at most.
/// </summary>
public static class SolverRegistry
{
    public static IReadOnlyList<ISolver> All { get; } =
    [
        new PrimalSimplexSolver(),
        new RevisedPrimalSimplexSolver(),
        new BranchAndBoundSimplexSolver(),
        new CuttingPlaneSolver(),
        new KnapsackBranchAndBoundSolver()
    ];
}
