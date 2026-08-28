using LPR381.Core.Algorithms;
using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>What a run of the simplex ended in.</summary>
/// <param name="Outcome">Optimal, Unbounded, Infeasible or Error.</param>
/// <param name="Tableau">The last table produced.</param>
/// <param name="Message">A sentence explaining the outcome.</param>
/// <param name="NextIndex">The next free table number, so a caller can carry on numbering.</param>
public sealed record SimplexRun(PivotOutcome Outcome, Tableau Tableau, string Message, int NextIndex)
{
    public bool IsOptimal => Outcome == PivotOutcome.Optimal;
}

/// <summary>
/// Drives a table to optimality: the Dual Simplex first if the table has negative RHS values, then
/// the Primal Simplex, exactly as the study notes prescribe.
/// <para>
/// Every algorithm in the project runs through here - Primal Simplex, Branch &amp; Bound (each
/// sub-problem), the Cutting Plane (after every cut) and the sensitivity operations that re-optimise
/// after a change. They differ only in the table they hand in and the label they want on it.
/// </para>
/// </summary>
public static class SimplexEngine
{
    /// <summary>Safety net: a correct model never needs anywhere near this many pivots.</summary>
    public const int MaxIterations = 100;

    /// <summary>
    /// Solves <paramref name="start"/> and appends a step for every table along the way.
    /// </summary>
    /// <param name="start">The table to solve. It is not modified.</param>
    /// <param name="sense">Max or min - this is what flips the optimality test.</param>
    /// <param name="steps">Collects one step per table, for display and export.</param>
    /// <param name="labelPrefix">Table naming, e.g. "T" gives T-1, T-2; "SP2.1 T" gives SP2.1 T-1.</param>
    /// <param name="startIndex">Number already used by <paramref name="start"/>.</param>
    public static SimplexRun Solve(
        Tableau start,
        ObjectiveSense sense,
        List<IterationStep> steps,
        string labelPrefix = "T",
        int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(steps);

        Tableau current = start;
        int index = startIndex;

        // Negative RHS values (from >= or = constraints, a Branch & Bound bound, or a cut) have to
        // be cleared with the Dual Simplex before the Primal Simplex can run.
        if (current.HasNegativeRightHandSide())
        {
            SimplexRun dual = RunPhase(new DualPivotRule(), sense, current, steps, labelPrefix, index);
            current = dual.Tableau;
            index = dual.NextIndex;

            // Dual unbounded means Primal infeasible - stop immediately
            // Dual infeasible or error also means we cannot continue
            if (dual.Outcome is PivotOutcome.Unbounded or PivotOutcome.Infeasible or PivotOutcome.Error)
            {
                // For Dual unbounded, report as Primal infeasible (the dual of unbounded is infeasible)
                PivotOutcome finalOutcome = dual.Outcome == PivotOutcome.Unbounded
                    ? PivotOutcome.Infeasible
                    : dual.Outcome;

                string message = dual.Outcome == PivotOutcome.Unbounded
                    ? "The Dual Simplex is unbounded, which means the Primal problem is infeasible. " + dual.Message
                    : dual.Message;

                return new SimplexRun(finalOutcome, dual.Tableau, message, index);
            }
        }

        return RunPhase(new PrimalPivotRule(), sense, current, steps, labelPrefix, index);
    }

         

    /// <summary>
    /// Applies one pivot rule until it says to stop, recording every table it pivoted on. The table
    /// shown with the pivot markers is the table the choice was made on - the result of that pivot is
    /// the next table in the list.
    /// </summary>
    public static SimplexRun RunPhase(
        IPivotRule rule,
        ObjectiveSense sense,
        Tableau start,
        List<IterationStep> steps,
        string labelPrefix,
        int startIndex)
    {
        Tableau current = start;
        int index = startIndex;

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            PivotDecision decision = rule.Choose(current, sense);

            if (decision.Outcome != PivotOutcome.Pivot)
            {
                return new SimplexRun(decision.Outcome, current, decision.Narrative, index);
            }

            steps.Add(new IterationStep(
                current,
                $"{current.Label} ({rule.Name})",
                decision.Narrative,
                decision.PivotRow,
                decision.PivotColumn,
                decision.Ratios,
                decision.RatioAxis,
                decision.RatioHeading));

            index++;
            current = PivotEngine.Pivot(current, decision.PivotRow, decision.PivotColumn, $"{labelPrefix}-{index}");
        }

        return new SimplexRun(
            PivotOutcome.Error,
            current,
            $"Stopped after {MaxIterations} iterations without reaching an optimal table. " +
            "This usually means the model is degenerate and the pivoting is cycling.",
            index);
    }

    /// <summary>
    /// Solves a table without recording anything. Used where only the answer matters, such as the
    /// bounding step of Branch &amp; Bound when a node is about to be fathomed anyway.
    /// </summary>
    public static SimplexRun SolveQuietly(Tableau start, ObjectiveSense sense) =>
        Solve(start, sense, [], start.Label, 0);
}
