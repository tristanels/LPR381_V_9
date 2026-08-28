using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// Builds the next table from the current one. This is Section 2 of the study notes,
/// "Building the Next Table", and it is deliberately the only place in the project where a
/// tableau is transformed - Primal Simplex, Dual Simplex, Branch &amp; Bound and Cutting Plane all
/// pivot through here, they only differ in <i>which</i> row and column they choose.
/// </summary>
public static class PivotEngine
{
    /// <summary>
    /// Pivots on the given row and column and returns a brand new table.
    /// <list type="number">
    /// <item>New pivot row = old pivot row / pivot element ("waar hulle kruis").</item>
    /// <item>Every other row = old row - (that row's pivot column value * the NEW pivot row).</item>
    /// </list>
    /// </summary>
    /// <param name="source">The table being pivoted.</param>
    /// <param name="pivotRow">Row index of the pivot element (never the z row).</param>
    /// <param name="pivotColumn">Column index of the pivot element.</param>
    /// <param name="label">Name for the new table, e.g. "T-2".</param>
    public static Tableau Pivot(Tableau source, int pivotRow, int pivotColumn, string label)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (pivotRow < Tableau.FirstConstraintRow || pivotRow >= source.RowCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pivotRow), "The pivot row must be a constraint row.");
        }

        if (pivotColumn < 0 || pivotColumn >= source.RhsColumn)
        {
            throw new ArgumentOutOfRangeException(nameof(pivotColumn), "The pivot column must be a variable column.");
        }

        double pivotElement = source[pivotRow, pivotColumn];
        if (Num.IsZero(pivotElement))
        {
            throw new InvalidOperationException("Cannot pivot on a zero element.");
        }

        double[,] values = source.CopyValues();
        int columnCount = source.ColumnCount;

        // Step 1: the new pivot row is the old pivot row divided by the pivot element.
        for (int column = 0; column < columnCount; column++)
        {
            values[pivotRow, column] = source[pivotRow, column] / pivotElement;
        }

        // Step 2: every other row (the z row included) subtracts its own pivot column value
        // multiplied by the NEW pivot row. That is what drives the pivot column to a unit column.
        for (int row = 0; row < source.RowCount; row++)
        {
            if (row == pivotRow)
        {
                continue;
            }

            double factor = source[row, pivotColumn];
            if (Num.IsZero(factor))
        {
                continue;
            }

            for (int column = 0; column < columnCount; column++)
        {
                values[row, column] = source[row, column] - (factor * values[pivotRow, column]);
            }
        }

        // The entering variable takes over the pivot row's place in the basis.
        int[] basis = (int[])source.Basis.Clone();
        basis[pivotRow] = pivotColumn;

        // The row label follows the basis, so the final table can be read straight off:
        // the row labelled x1 carries x1's value in the RHS column.
        string[] rowLabels = [.. source.RowLabels];
        if (pivotColumn < source.ColumnNames.Count)
        {
            rowLabels[pivotRow] = source.ColumnNames[pivotColumn];
        }

        return new Tableau(label, source.ColumnNames, rowLabels, values, basis);
    }
}
