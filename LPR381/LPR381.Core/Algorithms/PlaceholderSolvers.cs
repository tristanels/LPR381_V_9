using LPR381.Core.Models;

namespace LPR381.Core.Algorithms;

/// <summary>
/// Shared behaviour for the algorithms that are still to be written. They are real
/// <see cref="ISolver"/> implementations so they already appear in the menu, in the output file
/// header and in the registry - finishing one means replacing <see cref="Solve"/>, and nothing else
/// in the program has to be touched.
/// </summary>
public abstract class PlaceholderSolver : ISolver
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    /// <summary>What still has to be built, shown to the user so the gap is honest and obvious.</summary>
    protected abstract string OutstandingWork { get; }

    public bool IsImplemented => false;

    public virtual bool CanSolve(LinearModel model, out string reason)
    {
        reason = $"{Name} has not been implemented yet.";
        return false;
    }

    public SolveResult Solve(LinearModel model) =>
        SolveResult.NotImplemented(Name, $"{Name} is not implemented yet. Still to build: {OutstandingWork}");
}

/// <summary>
/// Brief: "Display the Canonical Form and solve using the Revised Primal Simplex Algorithm.
/// Display all Product Form and Price Out iterations."
/// </summary>
public sealed class RevisedPrimalSimplexSolver : PlaceholderSolver
{
    public override string Name => "Revised Primal Simplex Algorithm";

    public override string Description =>
        "Matrix form of the simplex: B-inverse, product form and price out at every iteration.";

    protected override string OutstandingWork =>
        "the B-inverse (product form) update, the CBV*B-inverse price out of each column, and the display of both per iteration.";
}

/// <summary>
/// Brief: Branch &amp; Bound Simplex with backtracking, all sub-problems created, all nodes fathomed,
/// every sub-problem table displayed, and the best candidate reported.
/// </summary>
public sealed class BranchAndBoundSimplexSolver : PlaceholderSolver
{
    public override string Name => "Branch & Bound Simplex Algorithm";

    public override string Description =>
        "Solves Integer Programming models by branching on fractional variables, with backtracking and fathoming.";

    protected override string OutstandingWork =>
        "branching variable selection (fraction closest to 0.5), the sub-problem tree with backtracking, " +
        "the row fix for a new constraint ((Top - Bottom) * -1 for <=, Top - Bottom for >=), node fathoming and best-candidate selection.";
}

/// <summary>
/// Brief: "Display the Canonical Form and solve using the Cutting Plane Algorithm."
/// Notes Section 5: pick the cut variable, split fractions from integers, add the cut, re-solve.
/// </summary>
public sealed class CuttingPlaneSolver : PlaceholderSolver
{
    public override string Name => "Cutting Plane Algorithm";

    public override string Description =>
        "Adds Gomory cuts to the relaxed LP until the answer comes out integer.";

    protected override string OutstandingWork =>
        "cut variable selection, splitting each row into integer and (always positive) fraction parts, " +
        "building the cut row with its slack, and re-solving with the Dual Simplex after every cut.";
}

/// <summary>
/// Brief: "Solve using Branch and Bound Knapsack algorithm" - worth 25 marks, the single biggest item.
/// Notes Section 12: ratio test, rank, fill, left/needed fraction, branch 0/1, force, candidate, best z.
/// </summary>
public sealed class KnapsackBranchAndBoundSolver : PlaceholderSolver
{
    public override string Name => "Branch & Bound Knapsack Algorithm";

    public override string Description =>
        "For binary models with a single capacity constraint: rank by z/c, fill the bag, branch on the fractional item.";

    protected override string OutstandingWork =>
        "the z/c ratio test and ranking, filling the bag in rank order, the left/needed fraction, " +
        "branching on the fractional variable with forced values inherited down each branch, and picking the best candidate.";

    public override bool CanSolve(LinearModel model, out string reason)
    {
        ArgumentNullException.ThrowIfNull(model);

        // The entry condition from the notes: ONE BAG = ONE CONSTRAINT, and every variable binary.
        if (!model.IsBinaryModel)
        {
            reason = "Knapsack Branch & Bound needs every decision variable to be binary (bin).";
            return false;
        }

        if (model.Constraints.Count != 1)
        {
            reason =
                $"Knapsack Branch & Bound works with exactly one constraint (one bag) but this model has " +
                $"{model.Constraints.Count}. Use Branch & Bound Simplex instead.";
            return false;
        }

        reason = $"{Name} has not been implemented yet.";
        return false;
    }
}
