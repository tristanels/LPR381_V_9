using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// One simplex table, laid out the same way as the Excel sheet in the study notes:
/// row 0 is the z row, every row after it is a constraint row, and the last column is the RHS.
/// <para>
/// A tableau is never edited in place. Each iteration produces a <b>new</b> tableau via
/// <see cref="PivotEngine"/>, which is what lets the program display T-0, T-1, T-2 ... side by side
/// instead of only the final answer.
/// </para>
/// </summary>
public sealed class Tableau
{
    private readonly double[,] _values;

    public Tableau(
        string label,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<string> rowLabels,
        double[,] values,
        int[] basis)
    {
        Label = label;
        ColumnNames = columnNames;
        RowLabels = rowLabels;
        _values = values;
        Basis = basis;

        if (rowLabels.Count != values.GetLength(0))
        {
            throw new ArgumentException("There must be one row label per row.", nameof(rowLabels));
        }

        if (columnNames.Count != values.GetLength(1))
        {
            throw new ArgumentException("There must be one column name per column.", nameof(columnNames));
        }
    }

    /// <summary>Table name shown in the output, e.g. "T-0" for the canonical form or "T-1" after one pivot.</summary>
    public string Label { get; }

    /// <summary>Column headings, ending with "rhs".</summary>
    public IReadOnlyList<string> ColumnNames { get; }

    /// <summary>Row headings: "z" for row 0, then "1", "2", ... for the constraint rows.</summary>
    public IReadOnlyList<string> RowLabels { get; }

    /// <summary>
    /// The column index of the basic variable of each row. Index 0 (the z row) is always -1.
    /// </summary>
    public int[] Basis { get; }

    public int RowCount => _values.GetLength(0);

    public int ColumnCount => _values.GetLength(1);

    /// <summary>Index of the RHS column - always the last one.</summary>
    public int RhsColumn => ColumnCount - 1;

    /// <summary>The z row is always row 0.</summary>
    public const int ObjectiveRow = 0;

    /// <summary>Index of the first constraint row.</summary>
    public const int FirstConstraintRow = 1;

    public double this[int row, int column] => _values[row, column];

    public double Rhs(int row) => _values[row, RhsColumn];

    /// <summary>The current objective value, which sits in the RHS of the z row.</summary>
    public double ObjectiveValue => _values[ObjectiveRow, RhsColumn];

    /// <summary>Copies the raw values out, for the pivot engine to work on.</summary>
    public double[,] CopyValues()
    {
        double[,] copy = new double[RowCount, ColumnCount];
        Array.Copy(_values, copy, _values.Length);
        return copy;
    }

    /// <summary>An exact copy under a new table name.</summary>
    public Tableau Clone(string label) =>
        new(label, ColumnNames, RowLabels, CopyValues(), (int[])Basis.Clone());

    /// <summary>
    /// The same table with different numbers in it. Used by the sensitivity operations, which change
    /// one cost or one right-hand-side and then re-optimise.
    /// </summary>
    public Tableau WithValues(double[,] values, string label) =>
        new(label, ColumnNames, RowLabels, values, (int[])Basis.Clone());

    /// <summary>
    /// Reads the value of every column out of the table: a basic variable takes the RHS of its row,
    /// a non-basic variable is zero.
    /// </summary>
    public IReadOnlyDictionary<string, double> ReadColumnValues()
    {
        Dictionary<string, double> values = [];

        for (int column = 0; column < RhsColumn; column++)
        {
            values[ColumnNames[column]] = 0.0;
        }

        for (int row = FirstConstraintRow; row < RowCount; row++)
        {
            int basisColumn = Basis[row];
            if (basisColumn >= 0 && basisColumn < RhsColumn)
            {
                values[ColumnNames[basisColumn]] = Rhs(row);
            }
        }

        return values;
    }

    /// <summary>True when any constraint row has a negative RHS, i.e. the table needs the Dual Simplex first.</summary>
    public bool HasNegativeRightHandSide()
    {
        for (int row = FirstConstraintRow; row < RowCount; row++)
        {
            if (Num.IsNegative(Rhs(row)))
            {
                return true;
            }
        }

        return false;
    }
}
