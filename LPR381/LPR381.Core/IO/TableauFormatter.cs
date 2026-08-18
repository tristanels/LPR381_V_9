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

        Tableau tableau = step.Tableau;
        bool showRatioColumn = step.Ratios is not null && step.RatioAxis == RatioAxis.Rows;

        int[] widths = MeasureColumns(tableau);
        int ratioWidth = Math.Max(step.RatioHeading.Length + 2, MinimumColumnWidth);

        writer.WriteLine(step.Heading);

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
