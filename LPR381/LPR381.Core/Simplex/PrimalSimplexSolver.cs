using System.Text;
using LPR381.Core.Algorithms;
using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// The Primal Simplex Algorithm as taught in Sections 1 and 2 of the study notes, keeping every
/// table so the whole working can be displayed and exported.
/// <para>
/// The notes say the entry condition for the Primal Simplex is that all constraints are &lt;=. When a
/// model contains a &gt;= or an = constraint the canonical form comes out with negative RHS values, and
/// the notes then say to run the Dual Simplex first and finish with the Primal. That is what
/// <see cref="SimplexEngine"/> does, and each table is labelled with the rule that produced it.
/// </para>
/// </summary>
public sealed class PrimalSimplexSolver : ISolver
{
    public string Name => "Primal Simplex Algorithm";

    public string Description =>
        "Tableau simplex, showing every iteration. Runs the Dual Simplex first when the canonical form has a negative RHS.";

    public bool IsImplemented => true;

    public bool CanSolve(LinearModel model, out string reason)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.IsNonLinear)
        {
            reason = "This model has a non-linear objective term (a ^ power). Write it as a function of one variable and solve it with the Golden Section Search - see sample golden.txt.";
            return false;
        }

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

        SimplexRun run = SimplexEngine.Solve(canonicalForm.InitialTableau, model.Sense, steps);

        switch (run.Outcome)
        {
            case PivotOutcome.Unbounded:
                steps.Add(new IterationStep(run.Tableau, $"{run.Tableau.Label} (final)", run.Message));
                return SolveResult.Failed(Name, SolutionStatus.Unbounded, run.Message, canonicalForm, steps);

            case PivotOutcome.Infeasible:
                steps.Add(new IterationStep(run.Tableau, $"{run.Tableau.Label} (final)", run.Message));
                return SolveResult.Failed(Name, SolutionStatus.Infeasible, run.Message, canonicalForm, steps);

            case PivotOutcome.Error:
                steps.Add(new IterationStep(run.Tableau, $"{run.Tableau.Label} (stopped)", run.Message));
                return SolveResult.Failed(Name, SolutionStatus.Error, run.Message, canonicalForm, steps);
        }

        string narrative = run.Message + DescribeExtras(run.Tableau, model, canonicalForm);
        steps.Add(new IterationStep(run.Tableau, $"{run.Tableau.Label} (OPTIMAL)", narrative));

        IReadOnlyDictionary<string, double> solution = canonicalForm.ReadSolution(run.Tableau);

        string message = model.IsIntegerModel
            ? "Optimal solution of the RELAXED LP found (int/bin restrictions have not been enforced)."
            : "Optimal solution found.";

        return SolveResult.Optimal(
            Name, canonicalForm, steps, run.Tableau.ObjectiveValue, solution, message, run.Tableau);
    }

    /// <summary>
    /// Extra observations worth printing under the optimal table: alternative optima, and a reminder
    /// when the answer still has to be made integer.
    /// </summary>
    internal static string DescribeExtras(Tableau tableau, LinearModel model, CanonicalForm canonicalForm)
    {
        var text = new StringBuilder();

        // A non-basic column sitting at zero in the z row means another optimal corner exists.
        List<string> alternates = [];
        for (int column = 0; column < tableau.RhsColumn; column++)
        {
            if (!tableau.Basis.Contains(column) && Num.IsZero(tableau[Tableau.ObjectiveRow, column]))
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
