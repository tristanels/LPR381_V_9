using System.Text;
using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// The Primal Simplex pivot choice, Section 1 Steps 3 to 5 of the study notes.
/// <list type="bullet">
/// <item>Optimal when the z row has no negative values (max) / no positive values (min), RHS excluded.</item>
/// <item>Pivot column: the largest negative (max) or largest positive (min) value in the z row.</item>
/// <item>Pivot row: ratio = RHS / pivot column value, take the smallest positive ratio.</item>
/// </list>
/// </summary>
public sealed class PrimalPivotRule : IPivotRule
{
    public string Name => "Primal Simplex";

    public PivotDecision Choose(Tableau tableau, ObjectiveSense sense)
    {
        ArgumentNullException.ThrowIfNull(tableau);

        int pivotColumn = SelectEnteringColumn(tableau, sense, out double enteringValue);

        if (pivotColumn < 0)
        {
            string test = sense == ObjectiveSense.Max
                ? "no negative values left in the z row"
                : "no positive values left in the z row";
            return PivotDecision.Stop(PivotOutcome.Optimal, $"Optimality test passed: {test}. This table is optimal.");
        }

        // Ratio test. NaN marks a row that cannot be used, so the printed ratio column stays blank there.
        double[] ratios = new double[tableau.RowCount];
        ratios[Tableau.ObjectiveRow] = double.NaN;

        int pivotRow = -1;
        double bestRatio = double.PositiveInfinity;

        for (int row = Tableau.FirstConstraintRow; row < tableau.RowCount; row++)
        {
            double columnValue = tableau[row, pivotColumn];

            if (!Num.IsPositive(columnValue))
            {
                // A zero or negative entry puts no limit on how far the entering variable can grow.
                ratios[row] = double.NaN;
                continue;
            }

            double ratio = tableau.Rhs(row) / columnValue;
            ratios[row] = ratio;

            if (ratio < bestRatio - Num.Epsilon)
            {
                bestRatio = ratio;
                pivotRow = row;
            }
        }

        if (pivotRow < 0)
        {
            return PivotDecision.Stop(
                PivotOutcome.Unbounded,
                $"Column {tableau.ColumnNames[pivotColumn]} has no positive entry, so there is no limit on how " +
                "far it can increase. The model is UNBOUNDED.");
        }

        var narrative = new StringBuilder();
        narrative.Append($"Pivot column {tableau.ColumnNames[pivotColumn]}: ");
        narrative.Append(sense == ObjectiveSense.Max ? "largest negative" : "largest positive");
        narrative.AppendLine($" value in the z row ({Num.Format(enteringValue)}).");
        narrative.Append($"Pivot row {tableau.RowLabels[pivotRow]}: smallest positive ratio ({Num.Format(bestRatio)}). ");
        narrative.Append($"Pivot element = {Num.Format(tableau[pivotRow, pivotColumn])}. ");
        narrative.Append($"{tableau.ColumnNames[pivotColumn]} enters the basis and ");
        narrative.Append($"{tableau.ColumnNames[tableau.Basis[pivotRow]]} leaves.");

        return new PivotDecision(
            PivotOutcome.Pivot,
            pivotRow,
            pivotColumn,
            ratios,
            RatioAxis.Rows,
            "ratio",
            narrative.ToString());
    }

    /// <summary>
    /// Picks the entering column: most negative z row value for a max problem, most positive for a min.
    /// Ties go to the left-most column, which keeps the iterations reproducible.
    /// </summary>
    private static int SelectEnteringColumn(Tableau tableau, ObjectiveSense sense, out double enteringValue)
    {
        int bestColumn = -1;
        double best = 0.0;

        for (int column = 0; column < tableau.RhsColumn; column++)
        {
            double value = tableau[Tableau.ObjectiveRow, column];

            bool better = sense == ObjectiveSense.Max
                ? Num.IsNegative(value) && value < best - Num.Epsilon
                : Num.IsPositive(value) && value > best + Num.Epsilon;

            if (better)
            {
                best = value;
                bestColumn = column;
            }
        }

        enteringValue = best;
        return bestColumn;
    }
}
