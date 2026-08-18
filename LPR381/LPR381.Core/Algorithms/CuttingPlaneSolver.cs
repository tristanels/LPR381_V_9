using System.Text;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Algorithms;

/// <summary>
/// The Cutting Plane Algorithm, Section 5 of the study notes.
/// <list type="number">
/// <item>Get a relaxed solution with the simplex.</item>
/// <item>Pick the cut variable - fraction closest to 0.5, smallest subscript breaks a tie.</item>
/// <item>Find the row where that basic variable's 1 sits and write the row out as an equation.</item>
/// <item>Split every coefficient into an integer part and a fraction, where the fraction must always
/// come out positive (so negatives round <i>down</i> to the next integer).</item>
/// <item>Throw the integers away, keep the fractions, and turn what is left into a cut row with its
/// own slack.</item>
/// <item>Add the cut to the table and solve again - the cut always arrives with a negative RHS, so
/// the Dual Simplex runs first.</item>
/// </list>
/// Repeat until every int/bin variable comes out whole.
/// </summary>
public sealed class CuttingPlaneSolver : ISolver
{
    /// <summary>Cuts get weaker as they pile up; this stops a runaway loop on a hard model.</summary>
    public const int MaxCuts = 20;

    public string Name => "Cutting Plane Algorithm";

    public string Description =>
        "Adds Gomory cuts to the relaxed LP until the answer comes out integer.";

    public bool IsImplemented => true;

    public bool CanSolve(LinearModel model, out string reason)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.IsNonLinear)
        {
            reason = "This model has a non-linear objective term (a ^ power). Use the Non-Linear solver.";
            return false;
        }

        if (!model.IsIntegerModel)
        {
            reason =
                "No variable is int or bin, so there is nothing to cut. " +
                "The Primal Simplex already gives the exact answer for this model.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public SolveResult Solve(LinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        CanonicalForm form = CanonicalFormBuilder.Build(model);
        List<IterationStep> steps = [];

        SimplexRun run = SimplexEngine.Solve(form.InitialTableau, model.Sense, steps, "T");

        if (run.Outcome != PivotOutcome.Optimal)
        {
            steps.Add(new IterationStep(run.Tableau, $"{run.Tableau.Label} (final)", run.Message));
            SolutionStatus status = run.Outcome switch
            {
                PivotOutcome.Unbounded => SolutionStatus.Unbounded,
                PivotOutcome.Infeasible => SolutionStatus.Infeasible,
                _ => SolutionStatus.Error
            };
            return SolveResult.Failed(Name, status, "The relaxed LP could not be solved: " + run.Message, form, steps);
        }

        Tableau current = run.Tableau;
        steps.Add(new IterationStep(
            current,
            $"{current.Label} (relaxed optimum)",
            run.Message + " " + Describe(model, form, current)));

        for (int cut = 1; cut <= MaxCuts; cut++)
        {
            FractionalVariable? target = IntegerRules.SelectBranchingVariable(model, form, current);

            if (target is null)
            {
                IReadOnlyDictionary<string, double> answer = form.ReadSolution(current);
                steps.Add(IterationStep.Text(
                    "INTEGER SOLUTION REACHED",
                    $"  z = {Num.Format(current.ObjectiveValue)}\n" +
                    string.Concat(model.Variables.Select(v => $"  {v.Name} = {Num.Format(answer[v.Name])}\n")),
                    $"Every int/bin variable is whole after {cut - 1} cut(s), so the algorithm stops."));

                return SolveResult.Optimal(
                    Name,
                    form,
                    steps,
                    current.ObjectiveValue,
                    answer,
                    $"Integer optimal solution found after {cut - 1} cut(s).",
                    current);
            }

            int column = form.ColumnOf(target.Variable.Name);
            int row = IntegerRules.RowOf(current, column);

            if (row < 0)
            {
                const string missing =
                    "The cut variable is not basic in the optimal table, so there is no row to build a cut from.";
                steps.Add(IterationStep.Text("Cutting Plane stopped", "  " + missing, missing));
                return SolveResult.Failed(Name, SolutionStatus.Error, missing, form, steps);
            }

            (double[] cutCoefficients, double cutRhs, string working) = BuildCut(current, row, target);

            string slackName = IntegerRules.NextLogicalName(current, form.DecisionColumnCount, isSlack: true);
            steps.Add(IterationStep.Text($"Cut {cut} - from row {current.RowLabels[row]}", working,
                $"The cut is added as a new row with slack {slackName}. Its RHS is negative, " +
                "so the Dual Simplex runs first when the table is solved again."));

            current = TableauOperations.AddConstraint(
                current,
                cutCoefficients,
                Relation.LessOrEqual,
                cutRhs,
                $"cut{cut}",
                slackName,
                $"C{cut} T-0");

            steps.Add(new IterationStep(current, $"{current.Label} (cut {cut} added)", string.Empty));

            SimplexRun again = SimplexEngine.Solve(current, model.Sense, steps, $"C{cut} T");

            if (again.Outcome != PivotOutcome.Optimal)
            {
                steps.Add(new IterationStep(again.Tableau, $"{again.Tableau.Label} (final)", again.Message));
                SolutionStatus status = again.Outcome == PivotOutcome.Infeasible
                    ? SolutionStatus.Infeasible
                    : SolutionStatus.Error;
                return SolveResult.Failed(
                    Name, status, $"After cut {cut} the model came out {status}: {again.Message}", form, steps);
            }

            current = again.Tableau;
            steps.Add(new IterationStep(
                current,
                $"{current.Label} (optimum after cut {cut})",
                again.Message + " " + Describe(model, form, current)));
        }

        string tooMany = $"Stopped after {MaxCuts} cuts without reaching an integer solution. Try Branch & Bound instead.";
        return SolveResult.Failed(Name, SolutionStatus.Error, tooMany, form, steps);
    }

    /// <summary>
    /// Builds one Gomory cut from a row: every coefficient is split into an integer part and a
    /// positive fraction, the integers are thrown away, and what is left becomes
    /// <c>-f_j * x_j + slack = -f_b</c>.
    /// </summary>
    private static (double[] Coefficients, double Rhs, string Working) BuildCut(
        Tableau tableau,
        int row,
        FractionalVariable target)
    {
        double[] coefficients = new double[tableau.RhsColumn];
        List<IReadOnlyList<string>> split = [];

        for (int column = 0; column < tableau.RhsColumn; column++)
        {
            double value = tableau[row, column];
            double integerPart = Math.Floor(value);
            double fraction = Num.FractionalPart(value);

            // The cut only keeps the fraction terms, moved to the left as -f_j.
            coefficients[column] = -fraction;

            if (!Num.IsZero(value) || !Num.IsZero(fraction))
            {
                split.Add(
                [
                    tableau.ColumnNames[column],
                    Num.Format(value),
                    Num.Format(integerPart),
                    Num.Format(fraction)
                ]);
            }
        }

        double rhsValue = tableau.Rhs(row);
        double rhsFraction = Num.FractionalPart(rhsValue);
        split.Add(["rhs", Num.Format(rhsValue), Num.Format(Math.Floor(rhsValue)), Num.Format(rhsFraction)]);

        var working = new StringBuilder();
        working.AppendLine(
            $"  {target.Variable.Name} = {Num.Format(target.Value)} has the fraction closest to 0.5, and it is basic " +
            $"in row {tableau.RowLabels[row]}.");
        working.AppendLine();
        working.AppendLine("  Row as an equation, with every coefficient split into integer + fraction");
        working.AppendLine("  (the fraction must always come out positive, so negatives round DOWN):");
        working.AppendLine();
        working.Append(MatrixFormatter.Table(["column", "value", "integer", "fraction"], split));
        working.AppendLine();
        working.AppendLine("  Throw the integers away and keep the fractions:");
        working.AppendLine("    " + DescribeCut(tableau, coefficients, rhsFraction));

        return (coefficients, -rhsFraction, working.ToString());
    }

    private static string DescribeCut(Tableau tableau, double[] coefficients, double rhsFraction)
    {
        var text = new StringBuilder();
        bool first = true;

        for (int column = 0; column < coefficients.Length; column++)
        {
            double fraction = -coefficients[column];
            if (Num.IsZero(fraction))
            {
                continue;
            }

            text.Append(first ? string.Empty : " + ");
            text.Append($"{Num.Format(fraction)}{tableau.ColumnNames[column]}");
            first = false;
        }

        if (first)
        {
            text.Append('0');
        }

        text.Append($" >= {Num.Format(rhsFraction)}");
        text.Append("   ->   in canonical form: ");

        first = true;
        for (int column = 0; column < coefficients.Length; column++)
        {
            if (Num.IsZero(coefficients[column]))
            {
                continue;
            }

            text.Append(first ? string.Empty : " ");
            text.Append($"{Num.Format(coefficients[column])}{tableau.ColumnNames[column]}");
            first = false;
        }

        text.Append($" + slack = {Num.Format(-rhsFraction)}");
        return text.ToString();
    }

    private static string Describe(LinearModel model, CanonicalForm form, Tableau tableau)
    {
        IReadOnlyDictionary<string, double> solution = form.ReadSolution(tableau);
        return "Solution: " +
               string.Join(", ", model.Variables.Select(v => $"{v.Name} = {Num.Format(solution[v.Name])}")) +
               $", z = {Num.Format(tableau.ObjectiveValue)}.";
    }
}
