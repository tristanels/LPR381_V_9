using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// Surgery on an existing table: adding a constraint row, and adding a new activity column.
/// <para>
/// Branch &amp; Bound (a bound on a variable), the Cutting Plane (a cut) and two of the sensitivity
/// operations all need the same thing - bolt a row onto an already optimal table and put it back in
/// canonical form. The study notes give it as the "fix the row" rule in Section 4 Step 3:
/// (Top - Bottom) * -1 for a new slack, Top - Bottom for a new excess. Both are the same operation:
/// eliminate whatever is already basic out of the new row, then make the new logical column +1.
/// </para>
/// </summary>
public static class TableauOperations
{
    /// <summary>
    /// Adds a constraint to a table and restores canonical form.
    /// </summary>
    /// <param name="source">The table to extend.</param>
    /// <param name="coefficients">Coefficients over the existing variable columns (RHS excluded).</param>
    /// <param name="relation">&lt;= adds a slack, &gt;= adds an excess. Equality is not supported here.</param>
    /// <param name="rhs">Right-hand-side of the new constraint.</param>
    /// <param name="rowLabel">Label for the new row.</param>
    /// <param name="logicalName">Name of the new slack or excess column.</param>
    /// <param name="newTableLabel">Label for the resulting table.</param>
    public static Tableau AddConstraint(
        Tableau source,
        IReadOnlyList<double> coefficients,
        Relation relation,
        double rhs,
        string rowLabel,
        string logicalName,
        string newTableLabel)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(coefficients);

        if (relation == Relation.Equal)
        {
            throw new ArgumentException(
                "Split an equality into a <= row and a >= row before adding it.", nameof(relation));
        }

        int oldColumns = source.ColumnCount;
        int oldRows = source.RowCount;
        int newColumn = source.RhsColumn;            // the logical column takes the RHS column's place
        int newRhsColumn = newColumn + 1;
        int newRow = oldRows;

        double[,] values = new double[oldRows + 1, oldColumns + 1];

        // Copy the old table, leaving a gap for the new logical column just before the RHS.
        for (int row = 0; row < oldRows; row++)
        {
            for (int column = 0; column < source.RhsColumn; column++)
            {
                values[row, column] = source[row, column];
            }

            values[row, newRhsColumn] = source.Rhs(row);
        }

        for (int column = 0; column < coefficients.Count; column++)
        {
            values[newRow, column] = coefficients[column];
        }

        values[newRow, newColumn] = relation == Relation.LessOrEqual ? 1.0 : -1.0;
        values[newRow, newRhsColumn] = rhs;

        // Restore canonical form: anything already basic must stay basic, so subtract it out of the
        // new row. This is the notes' Top - Bottom step, done for every basic variable at once.
        for (int row = Tableau.FirstConstraintRow; row < oldRows; row++)
        {
            int basisColumn = source.Basis[row];
            double factor = values[newRow, basisColumn];

            if (Num.IsZero(factor))
            {
                continue;
            }

            for (int column = 0; column <= newRhsColumn; column++)
            {
                values[newRow, column] -= factor * values[row, column];
            }
        }

        // A >= row leaves -1 under its excess. Multiplying the row by -1 makes that column basic,
        // which is the "* -1 puts it in canonical form" step - and is what produces the negative RHS
        // that sends the table to the Dual Simplex.
        if (Num.IsNegative(values[newRow, newColumn]))
        {
            for (int column = 0; column <= newRhsColumn; column++)
            {
                values[newRow, column] = -values[newRow, column];
            }
        }

        List<string> columnNames = [.. source.ColumnNames.Take(source.RhsColumn), logicalName, "rhs"];
        List<string> rowLabels = [.. source.RowLabels, rowLabel];

        int[] basis = new int[oldRows + 1];
        Array.Copy(source.Basis, basis, oldRows);
        basis[newRow] = newColumn;

        return new Tableau(newTableLabel, columnNames, rowLabels, values, basis);
    }

    /// <summary>
    /// Adds a new decision variable (a new activity) to a table. The column is priced out against the
    /// current basis, which is exactly what the "add a new activity" sensitivity operation needs:
    /// the new column becomes B-inverse * A_new, and its z row value becomes CBV * B-inverse * A_new - c.
    /// </summary>
    /// <param name="source">The optimal table to extend.</param>
    /// <param name="originalColumn">The new column as written against the original constraint rows.</param>
    /// <param name="objectiveCoefficient">The new variable's objective function coefficient.</param>
    /// <param name="canonicalForm">Used to find B-inverse under the starting basis columns.</param>
    /// <param name="columnName">Name for the new column.</param>
    /// <param name="newTableLabel">Label for the resulting table.</param>
    public static Tableau AddActivity(
        Tableau source,
        IReadOnlyList<double> originalColumn,
        double objectiveCoefficient,
        CanonicalForm canonicalForm,
        string columnName,
        string newTableLabel)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(canonicalForm);

        double[,] inverse = BasisMatrix.CurrentInverse(source, canonicalForm);
        int constraintCount = source.RowCount - Tableau.FirstConstraintRow;

        // The caller supplies the column the way the user wrote it. A row that was negated on the way
        // into canonical form needs its coefficient negated too.
        double[] canonicalColumn = new double[constraintCount];
        for (int row = 0; row < constraintCount; row++)
        {
            canonicalColumn[row] = canonicalForm.Rows[row].Negated ? -originalColumn[row] : originalColumn[row];
        }

        // B-inverse * A_new gives the column as it appears in the current table.
        double[] pricedColumn = BasisMatrix.Multiply(inverse, canonicalColumn);

        // The z row value is the reduced cost: CBV * B-inverse * A_new - c_new.
        double[] shadowPrices = BasisMatrix.ShadowPrices(source, canonicalForm);
        double reducedCost = BasisMatrix.Dot(shadowPrices, canonicalColumn) - objectiveCoefficient;

        int newColumn = source.RhsColumn;
        double[,] values = new double[source.RowCount, source.ColumnCount + 1];

        for (int row = 0; row < source.RowCount; row++)
        {
            for (int column = 0; column < source.RhsColumn; column++)
            {
                values[row, column] = source[row, column];
            }

            values[row, newColumn + 1] = source.Rhs(row);
        }

        values[Tableau.ObjectiveRow, newColumn] = reducedCost;
        for (int row = 0; row < constraintCount; row++)
        {
            values[row + Tableau.FirstConstraintRow, newColumn] = pricedColumn[row];
        }

        List<string> columnNames = [.. source.ColumnNames.Take(source.RhsColumn), columnName, "rhs"];

        return new Tableau(
            newTableLabel,
            columnNames,
            source.RowLabels,
            values,
            (int[])source.Basis.Clone());
    }
}
