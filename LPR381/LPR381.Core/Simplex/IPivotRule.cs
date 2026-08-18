using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>What the pivot rule decided to do with the current table.</summary>
public enum PivotOutcome
{
    /// <summary>A pivot row and column were chosen - build the next table.</summary>
    Pivot,

    /// <summary>The table passes the optimality test, stop here.</summary>
    Optimal,

    /// <summary>The entering column has no positive entry, so the model is unbounded.</summary>
    Unbounded,

    /// <summary>The pivot row has no usable entry, so the model is infeasible.</summary>
    Infeasible,

    /// <summary>Pivoting was stopped without an answer, e.g. because the iterations were cycling.</summary>
    Error
}

/// <summary>Whether the ratios were worked out down the rows or across the columns.</summary>
public enum RatioAxis
{
    /// <summary>One ratio per constraint row - the Primal Simplex ratio column.</summary>
    Rows,

    /// <summary>One ratio per column - the Dual Simplex ratio row.</summary>
    Columns
}

/// <summary>
/// The result of looking at one table: which row and column to pivot on, the ratios that were
/// worked out to get there, and a sentence explaining the choice for the output file.
/// </summary>
public sealed record PivotDecision(
    PivotOutcome Outcome,
    int PivotRow,
    int PivotColumn,
    double[]? Ratios,
    RatioAxis RatioAxis,
    string RatioHeading,
    string Narrative)
{
    public static PivotDecision Stop(PivotOutcome outcome, string narrative) =>
        new(outcome, -1, -1, null, RatioAxis.Rows, string.Empty, narrative);
}

/// <summary>
/// Chooses the pivot for one iteration. Primal and Dual Simplex differ only in this choice - the
/// arithmetic that follows is the same <see cref="PivotEngine"/> for both, which is why Branch &amp;
/// Bound and the Cutting Plane algorithm can be built on top without repeating any of it.
/// </summary>
public interface IPivotRule
{
    string Name { get; }

    PivotDecision Choose(Tableau tableau, ObjectiveSense sense);
}
