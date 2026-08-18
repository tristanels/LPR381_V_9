using System.Text;
using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// The Dual Simplex pivot choice, Section 3 of the study notes. It is used when the table has
/// negative RHS values, which is what a >= constraint leaves behind after its row is multiplied by -1.
/// <list type="bullet">
/// <item>Pivot row: the row with the smallest (most negative) RHS.</item>
/// <item>Ratio = z row value / pivot row value, worked out only for the negative entries of the
/// pivot row, using absolute values.</item>
/// <item>Pivot column: the smallest of those ratios.</item>
/// </list>
/// Once no negative RHS values are left, the Primal Simplex finishes the job.
/// </summary>
public sealed class DualPivotRule : IPivotRule
{
    public string Name => "Dual Simplex";

    public PivotDecision Choose(Tableau tableau, ObjectiveSense sense)
    {
        ArgumentNullException.ThrowIfNull(tableau);

        int pivotRow = -1;
        double mostNegative = 0.0;

        for (int row = Tableau.FirstConstraintRow; row < tableau.RowCount; row++)
        {
            double rhs = tableau.Rhs(row);
            if (Num.IsNegative(rhs) && rhs < mostNegative - Num.Epsilon)
            {
                mostNegative = rhs;
                pivotRow = row;
            }
        }

        if (pivotRow < 0)
        {
            return PivotDecision.Stop(
                PivotOutcome.Optimal,
                "No negative RHS values are left, so the table is feasible. Continue with the Primal Simplex.");
        }

        // Ratios are only taken over the negative entries of the pivot row.
        double[] ratios = new double[tableau.ColumnCount];
        Array.Fill(ratios, double.NaN);

        int pivotColumn = -1;
        double bestRatio = double.PositiveInfinity;

        for (int column = 0; column < tableau.RhsColumn; column++)
        {
            double rowValue = tableau[pivotRow, column];
            if (!Num.IsNegative(rowValue))
            {
                continue;
            }

            double ratio = Math.Abs(tableau[Tableau.ObjectiveRow, column] / rowValue);
            ratios[column] = ratio;

            if (ratio < bestRatio - Num.Epsilon)
            {
                bestRatio = ratio;
                pivotColumn = column;
            }
        }

        if (pivotColumn < 0)
        {
            return PivotDecision.Stop(
                PivotOutcome.Infeasible,
                $"Row {tableau.RowLabels[pivotRow]} has a negative RHS ({Num.Format(mostNegative)}) but no negative " +
                "entry to pivot on, so that row can never be satisfied. The model is INFEASIBLE.");
        }

        var narrative = new StringBuilder();
        narrative.AppendLine(
            $"Dual Simplex. Pivot row {tableau.RowLabels[pivotRow]}: most negative RHS ({Num.Format(mostNegative)}).");
        narrative.Append(
            $"Pivot column {tableau.ColumnNames[pivotColumn]}: smallest ratio |z row / pivot row| = {Num.Format(bestRatio)}. ");
        narrative.Append($"Pivot element = {Num.Format(tableau[pivotRow, pivotColumn])}. ");
        narrative.Append($"{tableau.ColumnNames[pivotColumn]} enters the basis and ");
        narrative.Append($"{tableau.ColumnNames[tableau.Basis[pivotRow]]} leaves.");

        return new PivotDecision(
            PivotOutcome.Pivot,
            pivotRow,
            pivotColumn,
            ratios,
            RatioAxis.Columns,
            "|z / row|",
            narrative.ToString());
    }
}
