using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// The matrix view of a table: B, B-inverse, CBV, CBV*B-inverse and the priced-out columns.
/// <para>
/// The trick that makes this cheap is that the starting table already holds an identity matrix under
/// the slack and excess columns. Every table after it is B-inverse times the starting table, so the
/// same columns in the <i>current</i> table hold B-inverse itself - no matrix inversion needed. The
/// Revised Simplex and every sensitivity operation read the numbers they need from here.
/// </para>
/// </summary>
public static class BasisMatrix
{
    /// <summary>Number of constraint rows in a table.</summary>
    public static int ConstraintCount(Tableau tableau) => tableau.RowCount - Tableau.FirstConstraintRow;

    /// <summary>
    /// B-inverse, read out of <paramref name="current"/> under the columns that formed the identity
    /// in the starting table.
    /// </summary>
    public static double[,] CurrentInverse(Tableau current, CanonicalForm canonicalForm)
    {
        int size = ConstraintCount(current);
        double[,] inverse = new double[size, size];

        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                inverse[row, column] = current[row + Tableau.FirstConstraintRow, canonicalForm.Rows[column].LogicalColumn];
            }
        }

        return inverse;
    }

    /// <summary>
    /// CBV * B-inverse, read out of the z row under the same columns. For a slack column this is the
    /// shadow price of that row. Signs are relative to the canonical rows, so a row that was negated
    /// (every &gt;= row) reports the negative of the price the user expects - use
    /// <see cref="ConstraintShadowPrices"/> for display.
    /// </summary>
    public static double[] ShadowPrices(Tableau current, CanonicalForm canonicalForm)
    {
        int size = ConstraintCount(current);
        double[] prices = new double[size];

        for (int row = 0; row < size; row++)
        {
            prices[row] = current[Tableau.ObjectiveRow, canonicalForm.Rows[row].LogicalColumn];
        }

        return prices;
    }

    /// <summary>
    /// The shadow prices as the user should read them, with the sign put back for any row that was
    /// multiplied by -1 on the way into canonical form.
    /// </summary>
    public static double[] ConstraintShadowPrices(Tableau current, CanonicalForm canonicalForm)
    {
        double[] prices = ShadowPrices(current, canonicalForm);

        for (int row = 0; row < prices.Length; row++)
        {
            if (canonicalForm.Rows[row].Negated)
            {
                prices[row] = -prices[row];
            }
        }

        return prices;
    }

    /// <summary>The objective coefficient of each column, taken from the starting table (z row holds -c).</summary>
    public static double[] ObjectiveCoefficients(CanonicalForm canonicalForm)
    {
        Tableau start = canonicalForm.InitialTableau;
        double[] costs = new double[start.RhsColumn];

        for (int column = 0; column < start.RhsColumn; column++)
        {
            costs[column] = -start[Tableau.ObjectiveRow, column];
        }

        return costs;
    }

    /// <summary>CBV: the objective coefficients of the variables that are currently basic, in row order.</summary>
    public static double[] BasicCosts(Tableau current, CanonicalForm canonicalForm)
    {
        double[] costs = ObjectiveCoefficients(canonicalForm);
        int size = ConstraintCount(current);
        double[] basicCosts = new double[size];

        for (int row = 0; row < size; row++)
        {
            int column = current.Basis[row + Tableau.FirstConstraintRow];
            basicCosts[row] = column >= 0 && column < costs.Length ? costs[column] : 0.0;
        }

        return basicCosts;
    }

    /// <summary>The names of the basic variables, in row order.</summary>
    public static string[] BasicNames(Tableau current)
    {
        int size = ConstraintCount(current);
        string[] names = new string[size];

        for (int row = 0; row < size; row++)
        {
            names[row] = current.ColumnNames[current.Basis[row + Tableau.FirstConstraintRow]];
        }

        return names;
    }

    /// <summary>Column <paramref name="column"/> of the original constraint matrix A, from the starting table.</summary>
    public static double[] OriginalColumn(CanonicalForm canonicalForm, int column)
    {
        Tableau start = canonicalForm.InitialTableau;
        int size = ConstraintCount(start);
        double[] values = new double[size];

        for (int row = 0; row < size; row++)
        {
            values[row] = start[row + Tableau.FirstConstraintRow, column];
        }

        return values;
    }

    /// <summary>The original right-hand-side vector b, from the starting table.</summary>
    public static double[] OriginalRhs(CanonicalForm canonicalForm)
    {
        Tableau start = canonicalForm.InitialTableau;
        int size = ConstraintCount(start);
        double[] values = new double[size];

        for (int row = 0; row < size; row++)
        {
            values[row] = start.Rhs(row + Tableau.FirstConstraintRow);
        }

        return values;
    }

    /// <summary>Matrix times vector.</summary>
    public static double[] Multiply(double[,] matrix, IReadOnlyList<double> vector)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        double[] result = new double[rows];

        for (int row = 0; row < rows; row++)
        {
            double sum = 0.0;
            for (int column = 0; column < columns; column++)
            {
                sum += matrix[row, column] * vector[column];
            }

            result[row] = sum;
        }

        return result;
    }

    /// <summary>Row vector times matrix.</summary>
    public static double[] Multiply(IReadOnlyList<double> vector, double[,] matrix)
    {
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        double[] result = new double[columns];

        for (int column = 0; column < columns; column++)
        {
            double sum = 0.0;
            for (int row = 0; row < rows; row++)
            {
                sum += vector[row] * matrix[row, column];
            }

            result[column] = sum;
        }

        return result;
    }

    /// <summary>Dot product of two vectors.</summary>
    public static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        double sum = 0.0;
        for (int i = 0; i < left.Count; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }

    /// <summary>An identity matrix of the given size.</summary>
    public static double[,] Identity(int size)
    {
        double[,] matrix = new double[size, size];
        for (int i = 0; i < size; i++)
        {
            matrix[i, i] = 1.0;
        }

        return matrix;
    }

    /// <summary>
    /// The product form update: E * inverse, where E is an identity matrix with one column replaced
    /// by the eta vector. This is how the Revised Simplex advances B-inverse without re-inverting.
    /// </summary>
    public static double[,] ApplyEta(double[,] eta, double[,] inverse)
    {
        int size = inverse.GetLength(0);
        double[,] result = new double[size, size];

        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                double sum = 0.0;
                for (int k = 0; k < size; k++)
                {
                    sum += eta[row, k] * inverse[k, column];
                }

                result[row, column] = sum;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the eta matrix for a pivot: an identity with column <paramref name="pivotRow"/> replaced
    /// by the eta vector worked out from the entering column.
    /// </summary>
    public static double[,] EtaMatrix(IReadOnlyList<double> enteringColumn, int pivotRow)
    {
        int size = enteringColumn.Count;
        double[,] eta = Identity(size);
        double pivotValue = enteringColumn[pivotRow];

        for (int row = 0; row < size; row++)
        {
            eta[row, pivotRow] = row == pivotRow
                ? 1.0 / pivotValue
                : -enteringColumn[row] / pivotValue;
        }

        return eta;
    }
}
