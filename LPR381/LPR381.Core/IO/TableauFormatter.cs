using System.Text;
using LPR381.Core.Algorithms;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.IO;

/// <summary>
/// Draws a simplex table as text, laid out like the Excel sheet the module works in: the z row on
/// top, the RHS on the right, the ratio column beside it, and markers on the pivot row and column.
/// It writes to any <see cref="TextWriter"/>, which is how the screen and the output file are
/// guaranteed to show exactly the same thing.
/// </summary>
public static class TableauFormatter
{
    private const int LabelWidth = 6;
    private const int MinimumColumnWidth = 9;

    public static void Write(TextWriter writer, IterationStep step)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(step);

        writer.WriteLine(step.Heading);

        if (step.Tableau is not null)
        {
            WriteTable(writer, step, step.Tableau);
        }

        if (!string.IsNullOrWhiteSpace(step.Body))
        {
            writer.WriteLine(step.Body.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(step.Narrative))
        {
            writer.WriteLine();
            foreach (string line in step.Narrative.Split(Environment.NewLine))
            {
                writer.WriteLine("  " + line.Trim());
            }
        }

        writer.WriteLine();
    }

    private static void WriteTable(TextWriter writer, IterationStep step, Tableau tableau)
    {
        bool showRatioColumn = step.Ratios is not null && step.RatioAxis == RatioAxis.Rows;

        int[] widths = MeasureColumns(tableau);
        int ratioWidth = Math.Max(step.RatioHeading.Length + 2, MinimumColumnWidth);

        // Header row.
        writer.Write(new string(' ', LabelWidth));
        for (int column = 0; column < tableau.ColumnCount; column++)
        {
            writer.Write(tableau.ColumnNames[column].PadLeft(widths[column]));
        }

        if (showRatioColumn)
        {
            writer.Write("  |");
            writer.Write(step.RatioHeading.PadLeft(ratioWidth));
        }

        writer.WriteLine();

        // Body.
        for (int row = 0; row < tableau.RowCount; row++)
        {
            string marker = row == step.PivotRow ? "->" : "  ";
            writer.Write((marker + " " + tableau.RowLabels[row]).PadRight(LabelWidth));

            for (int column = 0; column < tableau.ColumnCount; column++)
            {
                writer.Write(Num.Format(tableau[row, column]).PadLeft(widths[column]));
            }

            if (showRatioColumn)
            {
                writer.Write("  |");
                double ratio = step.Ratios![row];
                writer.Write((double.IsNaN(ratio) ? string.Empty : Num.Format(ratio)).PadLeft(ratioWidth));
            }

            writer.WriteLine();
        }

        // The Dual Simplex works its ratios across the columns, so they print as a row underneath.
        if (step.Ratios is not null && step.RatioAxis == RatioAxis.Columns)
        {
            writer.Write(step.RatioHeading.PadRight(LabelWidth));
            for (int column = 0; column < tableau.ColumnCount; column++)
            {
                double ratio = column < step.Ratios.Length ? step.Ratios[column] : double.NaN;
                writer.Write((double.IsNaN(ratio) ? string.Empty : Num.Format(ratio)).PadLeft(widths[column]));
            }

            writer.WriteLine();
        }

        // A caret under the pivot column, so the pivot element is unmistakable.
        if (step.HasPivot)
        {
            int offset = LabelWidth;
            for (int column = 0; column < step.PivotColumn; column++)
            {
                offset += widths[column];
            }

            writer.Write(new string(' ', offset + widths[step.PivotColumn] - 1));
            writer.WriteLine($"^ pivot column ({tableau.ColumnNames[step.PivotColumn]})");
        }
    }

    /// <summary>Each column is made just wide enough for its heading and its widest value.</summary>
    private static int[] MeasureColumns(Tableau tableau)
    {
        int[] widths = new int[tableau.ColumnCount];

        for (int column = 0; column < tableau.ColumnCount; column++)
        {
            int width = tableau.ColumnNames[column].Length;

            for (int row = 0; row < tableau.RowCount; row++)
            {
                width = Math.Max(width, Num.Format(tableau[row, column]).Length);
            }

            widths[column] = Math.Max(width + 2, MinimumColumnWidth);
        }

        return widths;
    }
}

/// <summary>
/// Prints matrices and vectors. The Revised Primal Simplex has to display B-inverse, CBV*B-inverse
/// and each priced-out column, and the sensitivity operations show the same kind of working.
/// </summary>
public static class MatrixFormatter
{
    private const int CellWidth = 10;

    /// <summary>Renders a matrix with an optional title and row/column labels.</summary>
    public static string Matrix(
        string title,
        double[,] matrix,
        IReadOnlyList<string>? rowLabels = null,
        IReadOnlyList<string>? columnLabels = null)
    {
        var text = new StringBuilder();
        int rows = matrix.GetLength(0);
        int columns = matrix.GetLength(1);
        int labelWidth = rowLabels?.Max(label => label.Length) + 2 ?? 4;

        if (title.Length > 0)
        {
            text.AppendLine("  " + title);
        }

        if (columnLabels is not null)
        {
            text.Append(new string(' ', labelWidth + 2));
            foreach (string label in columnLabels)
            {
                text.Append(label.PadLeft(CellWidth));
            }

            text.AppendLine();
        }

        for (int row = 0; row < rows; row++)
        {
            text.Append("  ");
            text.Append((rowLabels is not null ? rowLabels[row] : string.Empty).PadRight(labelWidth));

            for (int column = 0; column < columns; column++)
            {
                text.Append(Num.Format(matrix[row, column]).PadLeft(CellWidth));
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Renders a vector on one line, with an optional label per entry.</summary>
    public static string Vector(string title, IReadOnlyList<double> values, IReadOnlyList<string>? labels = null)
    {
        var text = new StringBuilder();

        if (title.Length > 0)
        {
            text.AppendLine("  " + title);
        }

        if (labels is not null)
        {
            text.Append("    ");
            foreach (string label in labels)
            {
                text.Append(label.PadLeft(CellWidth));
            }

            text.AppendLine();
        }

        text.Append("    ");
        foreach (double value in values)
        {
            text.Append(Num.Format(value).PadLeft(CellWidth));
        }

        text.AppendLine();
        return text.ToString();
    }

    /// <summary>Renders a simple table of strings with right-aligned columns.</summary>
    public static string Table(IReadOnlyList<string> headings, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        int[] widths = new int[headings.Count];
        for (int column = 0; column < headings.Count; column++)
        {
            widths[column] = headings[column].Length;
            foreach (IReadOnlyList<string> row in rows)
            {
                widths[column] = Math.Max(widths[column], row[column].Length);
            }

            widths[column] += 3;
        }

        var text = new StringBuilder();
        text.Append("  ");
        for (int column = 0; column < headings.Count; column++)
        {
            text.Append(headings[column].PadLeft(widths[column]));
        }

        text.AppendLine();

        foreach (IReadOnlyList<string> row in rows)
        {
            text.Append("  ");
            for (int column = 0; column < row.Count; column++)
            {
                text.Append(row[column].PadLeft(widths[column]));
            }

            text.AppendLine();
        }

        return text.ToString();
    }
}
