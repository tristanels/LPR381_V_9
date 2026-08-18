using System.Text;
using LPR381.Core.Algorithms;
using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// The Primal Simplex Algorithm as taught in Section 1 and 2 of the study notes, keeping every
/// table so the whole working can be displayed and exported.
/// <para>
/// The notes say the entry condition for the Primal Simplex is that all constraints are &lt;=. When a
/// model contains a &gt;= or an = constraint, the canonical form comes out with negative RHS values,
/// and the notes then say to run the Dual Simplex first and finish with the Primal. This solver does
/// exactly that, and labels each table with the rule that produced it.
/// </para>
/// </summary>
public sealed class PrimalSimplexSolver : ISolver
{
    /// <summary>Safety net: a correct model never needs anywhere near this many pivots.</summary>
    public const int MaxIterations = 100;

    public string Name => "Primal Simplex Algorithm";

    public string Description =>
        "Tableau simplex, showing every iteration. Runs the Dual Simplex first when the canonical form has a negative RHS.";

    public bool IsImplemented => true;

    public bool CanSolve(LinearModel model, out string reason)
    {
        ArgumentNullException.ThrowIfNull(model);

        reason = model.IsIntegerModel
            ? "This model has int/bin restrictions. The Primal Simplex solves the RELAXED LP - use Branch & Bound for the integer answer."
            : string.Empty;

        return true;
    }

    public SolveResult Solve(LinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        CanonicalForm canonicalForm = CanonicalFormBuilder.Build(model);
        List<IterationStep> steps = [];
        int tableIndex = 0;
        Tableau current = canonicalForm.InitialTableau;

        // The notes: negative RHS values (from >= or = constraints) must be cleared with the Dual
        // Simplex before the Primal Simplex can run.
        if (current.HasNegativeRightHandSide())
        {
            PhaseResult dual = RunPhase(new DualPivotRule(), model.Sense, current, steps, ref tableIndex);
            current = dual.Tableau;

            if (dual.Outcome == PivotOutcome.Infeasible)
            {
                steps.Add(new IterationStep(current, $"{current.Label} (final)", dual.Message));
                return SolveResult.Failed(Name, SolutionStatus.Infeasible, dual.Message, canonicalForm, steps);
            }

            if (dual.Outcome == PivotOutcome.Error)
            {
                steps.Add(new IterationStep(current, $"{current.Label} (stopped)", dual.Message));
                return SolveResult.Failed(Name, SolutionStatus.Error, dual.Message, canonicalForm, steps);
            }
        }

        PhaseResult primal = RunPhase(new PrimalPivotRule(), model.Sense, current, steps, ref tableIndex);
        current = primal.Tableau;

        switch (primal.Outcome)
        {
            case PivotOutcome.Unbounded:
                steps.Add(new IterationStep(current, $"{current.Label} (final)", primal.Message));
                return SolveResult.Failed(Name, SolutionStatus.Unbounded, primal.Message, canonicalForm, steps);

            case PivotOutcome.Error:
                steps.Add(new IterationStep(current, $"{current.Label} (stopped)", primal.Message));
                return SolveResult.Failed(Name, SolutionStatus.Error, primal.Message, canonicalForm, steps);
        }

        // Optimal.
        string narrative = primal.Message + DescribeExtras(current, model, canonicalForm);
        steps.Add(new IterationStep(current, $"{current.Label} (OPTIMAL)", narrative));

        IReadOnlyDictionary<string, double> solution = canonicalForm.ReadSolution(current);

        string message = model.IsIntegerModel
            ? "Optimal solution of the RELAXED LP found (int/bin restrictions have not been enforced)."
            : "Optimal solution found.";

        return SolveResult.Optimal(Name, canonicalForm, steps, current.ObjectiveValue, solution, message);
    }

    private readonly record struct PhaseResult(PivotOutcome Outcome, Tableau Tableau, string Message);

    /// <summary>
    /// Applies one pivot rule until it says to stop, recording every table it pivoted on along the way.
    /// The table shown with a pivot marker is the table the choice was made on - the result of that
    /// pivot is the next table in the list.
    /// </summary>
    private static PhaseResult RunPhase(
        IPivotRule rule,
        ObjectiveSense sense,
        Tableau start,
        List<IterationStep> steps,
        ref int tableIndex)
    {
        Tableau current = start;

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            PivotDecision decision = rule.Choose(current, sense);

            if (decision.Outcome != PivotOutcome.Pivot)
            {
                return new PhaseResult(decision.Outcome, current, decision.Narrative);
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

            tableIndex++;
            current = PivotEngine.Pivot(current, decision.PivotRow, decision.PivotColumn, $"T-{tableIndex}");
        }

        return new PhaseResult(
            PivotOutcome.Error,
            current,
            $"Stopped after {MaxIterations} iterations without reaching an optimal table. " +
            "This usually means the model is degenerate and the pivoting is cycling.");
    }

    /// <summary>
    /// Extra observations worth printing under the optimal table: alternative optima, and a reminder
    /// when the answer still has to be made integer.
    /// </summary>
    private static string DescribeExtras(Tableau tableau, LinearModel model, CanonicalForm canonicalForm)
    {
        var text = new StringBuilder();

        // A non-basic column sitting at zero in the z row means another optimal corner exists.
        List<string> alternates = [];
        for (int column = 0; column < tableau.RhsColumn; column++)
        {
            bool isBasic = tableau.Basis.Contains(column);
            if (!isBasic && Num.IsZero(tableau[Tableau.ObjectiveRow, column]))
            {
                alternates.Add(tableau.ColumnNames[column]);
            }
        }

        if (alternates.Count > 0)
        {
            text.Append(
                $" Note: {string.Join(", ", alternates)} is non-basic with a zero value in the z row, " +
                "so this model has ALTERNATIVE OPTIMAL SOLUTIONS.");
        }

        if (model.IsIntegerModel)
        {
            IReadOnlyDictionary<string, double> solution = canonicalForm.ReadSolution(tableau);
            List<string> fractional = model.Variables
                .Where(v => v.IsDiscrete && !Num.IsInteger(solution[v.Name]))
                .Select(v => $"{v.Name} = {Num.Format(solution[v.Name])}")
                .ToList();

            if (fractional.Count > 0)
            {
                text.Append(
                    $" This is the relaxed LP answer and {string.Join(", ", fractional)} came out fractional, " +
                    "so Branch & Bound or a Cutting Plane is still needed.");
            }
        }

        return text.ToString();
    }
}
