using System.Text;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Algorithms;

/// <summary>
/// The Revised Primal Simplex Algorithm: the same pivots as the tableau method, but carried out in
/// matrix form. The brief asks for the canonical form plus "all Product Form and Price Out
/// iterations", so every iteration prints B-inverse, CBV, CBV*B-inverse, the priced-out columns, the
/// entering column B-inverse*A_j, B-inverse*b, the ratio test and the eta matrix of the product form
/// update - followed by the equivalent tableau, so the two methods can be checked against each other.
/// <para>
/// It reuses <see cref="CanonicalFormBuilder"/> for the starting table and <see cref="BasisMatrix"/>
/// for the matrix reads, and it falls back on the Dual Simplex first when the canonical form has a
/// negative RHS, exactly as the tableau version does.
/// </para>
/// </summary>
public sealed class RevisedPrimalSimplexSolver : ISolver
{
    public string Name => "Revised Primal Simplex Algorithm";

    public string Description =>
        "Matrix form of the simplex: B-inverse, product form and price out shown at every iteration.";

    public bool IsImplemented => true;

    public bool CanSolve(LinearModel model, out string reason)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.IsNonLinear)
        {
            reason = "This model has a non-linear objective term (a ^ power). Use the Non-Linear solver.";
            return false;
        }

        reason = model.IsIntegerModel
            ? "This model has int/bin restrictions. The Revised Simplex solves the RELAXED LP - use Branch & Bound for the integer answer."
            : string.Empty;

        return true;
    }

    public SolveResult Solve(LinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        CanonicalForm form = CanonicalFormBuilder.Build(model);
        List<IterationStep> steps = [];

        Tableau start = form.InitialTableau;

        // The revised method assumes a feasible starting basis. When the canonical form has negative
        // RHS values, the Dual Simplex clears them first and hands over a feasible basis.
        if (start.HasNegativeRightHandSide())
        {
            SimplexRun dual = SimplexEngine.RunPhase(new DualPivotRule(), model.Sense, start, steps, "T", 0);

            if (dual.Outcome is PivotOutcome.Infeasible or PivotOutcome.Error)
            {
                steps.Add(new IterationStep(dual.Tableau, $"{dual.Tableau.Label} (final)", dual.Message));
                SolutionStatus status = dual.Outcome == PivotOutcome.Infeasible
                    ? SolutionStatus.Infeasible
                    : SolutionStatus.Error;
                return SolveResult.Failed(Name, status, dual.Message, form, steps);
            }

            start = dual.Tableau;
        }

        return RunRevised(model, form, start, steps);
    }

    private SolveResult RunRevised(LinearModel model, CanonicalForm form, Tableau start, List<IterationStep> steps)
    {
        int constraintCount = BasisMatrix.ConstraintCount(start);
        int columnCount = start.RhsColumn;

        double[] costs = BasisMatrix.ObjectiveCoefficients(form);
        double[] rhs = BasisMatrix.OriginalRhs(form);

        // A, taken from the canonical form once and never changed - the revised method works on the
        // ORIGINAL columns and reaches the current table through B-inverse.
        double[][] columns = new double[columnCount][];
        for (int column = 0; column < columnCount; column++)
        {
            columns[column] = BasisMatrix.OriginalColumn(form, column);
        }

        double[,] inverse = BasisMatrix.CurrentInverse(start, form);
        int[] basis = (int[])start.Basis.Clone();

        for (int iteration = 1; iteration <= SimplexEngine.MaxIterations; iteration++)
        {
            double[] basicCosts = new double[constraintCount];
            for (int row = 0; row < constraintCount; row++)
            {
                basicCosts[row] = costs[basis[row + Tableau.FirstConstraintRow]];
            }

            // Price out: y = CBV * B-inverse, then c_j* = y * A_j - c_j for every column.
            double[] y = BasisMatrix.Multiply(basicCosts, inverse);
            double[] reducedCosts = new double[columnCount];
            for (int column = 0; column < columnCount; column++)
            {
                reducedCosts[column] = BasisMatrix.Dot(y, columns[column]) - costs[column];
            }

            double[] basicValues = BasisMatrix.Multiply(inverse, rhs);
            Tableau equivalent = BuildTableau(form, start, inverse, y, basis, columns, rhs, $"T-{iteration - 1}");

            int entering = SelectEntering(reducedCosts, basis, model.Sense);
            var body = new StringBuilder();
            body.Append(DescribeIteration(form, start, inverse, basicCosts, y, reducedCosts, basicValues, basis, columnCount));

            if (entering < 0)
            {
                steps.Add(IterationStep.Text(
                    $"Revised iteration {iteration} - PRICE OUT (optimal)",
                    body.ToString(),
                    model.Sense == ObjectiveSense.Max
                        ? "No priced-out column is negative, so no column can improve z. This basis is optimal."
                        : "No priced-out column is positive, so no column can improve z. This basis is optimal."));

                steps.Add(new IterationStep(
                    equivalent,
                    $"{equivalent.Label} (OPTIMAL - equivalent tableau)",
                    "The same answer the tableau method reaches, rebuilt as B-inverse * [A | b]." +
                    PrimalSimplexSolver.DescribeExtras(equivalent, model, form)));

                IReadOnlyDictionary<string, double> solution = form.ReadSolution(equivalent);
                string message = model.IsIntegerModel
                    ? "Optimal solution of the RELAXED LP found (int/bin restrictions have not been enforced)."
                    : "Optimal solution found.";

                return SolveResult.Optimal(
                    Name, form, steps, equivalent.ObjectiveValue, solution, message, equivalent);
            }

            // B-inverse * A_j is the entering column as it appears in the current table.
            double[] enteringColumn = BasisMatrix.Multiply(inverse, columns[entering]);

            body.AppendLine(MatrixFormatter.Vector(
                $"Entering column A*{form.InitialTableau.ColumnNames[entering]} = B-inverse x A_j",
                enteringColumn,
                BasisMatrix.BasicNames(equivalent)));

            int leavingRow = RatioTest(basicValues, enteringColumn, out double[] ratios, out double bestRatio);

            body.AppendLine(MatrixFormatter.Vector("Ratio test (B-inverse x b) / (B-inverse x A_j)", ratios));

            if (leavingRow < 0)
            {
                string unbounded =
                    $"Column {start.ColumnNames[entering]} prices out as attractive but B-inverse * A_j has no " +
                    "positive entry, so nothing stops it increasing. The model is UNBOUNDED.";
                steps.Add(IterationStep.Text($"Revised iteration {iteration} - RATIO TEST", body.ToString(), unbounded));
                return SolveResult.Failed(Name, SolutionStatus.Unbounded, unbounded, form, steps);
            }

            // Product form: B-inverse is advanced by multiplying with an eta matrix, never re-inverted.
            double[,] eta = BasisMatrix.EtaMatrix(enteringColumn, leavingRow);
            body.AppendLine(MatrixFormatter.Matrix(
                $"Product form: eta matrix E (column {leavingRow + 1} replaced)", eta));

            string leavingName = start.ColumnNames[basis[leavingRow + Tableau.FirstConstraintRow]];
            string narrative =
                $"Price out picks {start.ColumnNames[entering]} (c* = {Num.Format(reducedCosts[entering])}). " +
                $"The ratio test picks row {leavingRow + 1} ({Num.Format(bestRatio)}), so {leavingName} leaves. " +
                $"B-inverse is updated by the product form: B-inverse(new) = E x B-inverse(old).";

            steps.Add(IterationStep.Text($"Revised iteration {iteration} - PRICE OUT and PRODUCT FORM", body.ToString(), narrative));
            steps.Add(new IterationStep(
                equivalent,
                $"{equivalent.Label} (equivalent tableau)",
                $"Pivot on row {leavingRow + 1}, column {start.ColumnNames[entering]}.",
                leavingRow + Tableau.FirstConstraintRow,
                entering));

            inverse = BasisMatrix.ApplyEta(eta, inverse);
            basis[leavingRow + Tableau.FirstConstraintRow] = entering;
        }

        const string cycling = "Stopped without reaching an optimal basis - the iterations look to be cycling.";
        return SolveResult.Failed(Name, SolutionStatus.Error, cycling, form, steps);
    }

    /// <summary>Most negative priced-out column for a max problem, most positive for a min.</summary>
    private static int SelectEntering(double[] reducedCosts, int[] basis, ObjectiveSense sense)
    {
        int best = -1;
        double bestValue = 0.0;

        for (int column = 0; column < reducedCosts.Length; column++)
        {
            if (basis.Contains(column))
            {
                continue;
            }

            double value = reducedCosts[column];
            bool better = sense == ObjectiveSense.Max
                ? Num.IsNegative(value) && value < bestValue - Num.Epsilon
                : Num.IsPositive(value) && value > bestValue + Num.Epsilon;

            if (better)
            {
                bestValue = value;
                best = column;
            }
        }

        return best;
    }

    /// <summary>Smallest positive ratio of B-inverse*b to the entering column.</summary>
    private static int RatioTest(double[] basicValues, double[] enteringColumn, out double[] ratios, out double bestRatio)
    {
        ratios = new double[basicValues.Length];
        Array.Fill(ratios, double.NaN);

        int best = -1;
        bestRatio = double.PositiveInfinity;

        for (int row = 0; row < basicValues.Length; row++)
        {
            if (!Num.IsPositive(enteringColumn[row]))
            {
                continue;
            }

            double ratio = basicValues[row] / enteringColumn[row];
            ratios[row] = ratio;

            if (ratio < bestRatio - Num.Epsilon)
            {
                bestRatio = ratio;
                best = row;
            }
        }

        return best;
    }

    /// <summary>The B-inverse / CBV / price out block that the brief asks to be displayed each iteration.</summary>
    private static string DescribeIteration(
        CanonicalForm form,
        Tableau start,
        double[,] inverse,
        double[] basicCosts,
        double[] shadowPrices,
        double[] reducedCosts,
        double[] basicValues,
        int[] basis,
        int columnCount)
    {
        string[] basicNames = new string[basicCosts.Length];
        for (int row = 0; row < basicCosts.Length; row++)
        {
            basicNames[row] = start.ColumnNames[basis[row + Tableau.FirstConstraintRow]];
        }

        var text = new StringBuilder();
        text.AppendLine($"  Basis (BV) : {string.Join(", ", basicNames)}");
        text.AppendLine();
        text.Append(MatrixFormatter.Matrix("B-inverse", inverse, basicNames));
        text.AppendLine();
        text.Append(MatrixFormatter.Vector("CBV (objective coefficients of the basic variables)", basicCosts, basicNames));
        text.AppendLine();
        text.Append(MatrixFormatter.Vector("CBV x B-inverse (shadow prices)", shadowPrices));
        text.AppendLine();
        text.Append(MatrixFormatter.Vector("B-inverse x b (values of the basic variables)", basicValues, basicNames));
        text.AppendLine();

        List<IReadOnlyList<string>> rows = [];
        for (int column = 0; column < columnCount; column++)
        {
            rows.Add(
            [
                start.ColumnNames[column],
                basis.Contains(column) ? "basic" : "non-basic",
                Num.Format(reducedCosts[column])
            ]);
        }

        text.AppendLine("  Price out: c_j* = (CBV x B-inverse x A_j) - c_j");
        text.Append(MatrixFormatter.Table(["column", "status", "c_j*"], rows));
        text.AppendLine();

        return text.ToString();
    }

    /// <summary>
    /// Rebuilds the tableau the revised method is standing on: B-inverse * [A | b], with the z row
    /// holding the priced-out values. Printing it lets the two methods be compared cell for cell.
    /// </summary>
    private static Tableau BuildTableau(
        CanonicalForm form,
        Tableau start,
        double[,] inverse,
        double[] shadowPrices,
        int[] basis,
        double[][] columns,
        double[] rhs,
        string label)
    {
        int constraintCount = BasisMatrix.ConstraintCount(start);
        int columnCount = start.RhsColumn;
        double[] costs = BasisMatrix.ObjectiveCoefficients(form);
        double[,] values = new double[constraintCount + 1, columnCount + 1];

        for (int column = 0; column < columnCount; column++)
        {
            double[] priced = BasisMatrix.Multiply(inverse, columns[column]);
            for (int row = 0; row < constraintCount; row++)
            {
                values[row + Tableau.FirstConstraintRow, column] = priced[row];
            }

            values[Tableau.ObjectiveRow, column] = BasisMatrix.Dot(shadowPrices, columns[column]) - costs[column];
        }

        double[] basicValues = BasisMatrix.Multiply(inverse, rhs);
        for (int row = 0; row < constraintCount; row++)
        {
            values[row + Tableau.FirstConstraintRow, columnCount] = basicValues[row];
        }

        values[Tableau.ObjectiveRow, columnCount] = BasisMatrix.Dot(shadowPrices, rhs);

        return new Tableau(label, start.ColumnNames, start.RowLabels, values, (int[])basis.Clone());
    }
}
