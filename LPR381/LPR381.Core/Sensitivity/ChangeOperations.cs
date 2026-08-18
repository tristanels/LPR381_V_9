using System.Text;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Sensitivity;

/// <summary>
/// Brief: "Apply and display a change of a selected Non-Basic Variable."
/// Only the z row entry of that column moves, because a non-basic column contributes nothing else.
/// </summary>
public sealed class NonBasicVariableChangeOperation : SensitivityOperation
{
    public override string Name => "Change a Non-Basic Variable";

    public override string Description =>
        "Apply a new objective coefficient to a non-basic variable and re-optimise.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        int column = PickNonBasicColumn(context, input, "Which non-basic variable?");
        if (column < 0)
        {
            return Cancelled;
        }

        double oldCoefficient = context.ObjectiveCoefficients[column];
        if (!input.TryReadNumber(
                $"New objective coefficient for {context.ColumnName(column)} (currently {Num.Format(oldCoefficient)}): ",
                out double newCoefficient))
        {
            return Cancelled;
        }

        double delta = newCoefficient - oldCoefficient;
        double[,] values = context.Tableau.CopyValues();

        // c_j* = z_j - c_j, so raising c_j by delta lowers the z row entry by delta.
        values[Tableau.ObjectiveRow, column] -= delta;
        Tableau changed = context.Tableau.WithValues(values, context.Tableau.Label + "*");

        var text = new StringBuilder();
        text.Append(context.Header($"Change the objective coefficient of {context.ColumnName(column)}"));
        text.AppendLine($"  Old coefficient = {Num.Format(oldCoefficient)}");
        text.AppendLine($"  New coefficient = {Num.Format(newCoefficient)}   (change of {Num.Format(delta)})");
        text.AppendLine($"  New z row value = {Num.Format(values[Tableau.ObjectiveRow, column])}");
        text.AppendLine();
        text.Append(context.ReOptimise(changed, out _));

        return text.ToString();
    }
}

/// <summary>
/// Brief: "Apply and display a change of a selected Basic Variable."
/// Changing the cost of a basic variable moves the whole z row by delta times that variable's row.
/// </summary>
public sealed class BasicVariableChangeOperation : SensitivityOperation
{
    public override string Name => "Change a Basic Variable";

    public override string Description =>
        "Apply a new objective coefficient to a basic variable and re-optimise.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        int column = PickBasicColumn(context, input, "Which basic variable?");
        if (column < 0)
        {
            return Cancelled;
        }

        int row = context.RowOf(column);
        double oldCoefficient = context.ObjectiveCoefficients[column];

        if (!input.TryReadNumber(
                $"New objective coefficient for {context.ColumnName(column)} (currently {Num.Format(oldCoefficient)}): ",
                out double newCoefficient))
        {
            return Cancelled;
        }

        double delta = newCoefficient - oldCoefficient;
        double[,] values = context.Tableau.CopyValues();

        // CBV changes in one place, so the z row picks up delta times that variable's row - which is
        // the same as rebuilding it as CBV x B-inverse x A - c.
        for (int c = 0; c < context.Tableau.ColumnCount; c++)
        {
            values[Tableau.ObjectiveRow, c] += delta * context.Tableau[row, c];
        }

        Tableau changed = context.Tableau.WithValues(values, context.Tableau.Label + "*");

        var text = new StringBuilder();
        text.Append(context.Header($"Change the objective coefficient of {context.ColumnName(column)}"));
        text.AppendLine($"  {context.ColumnName(column)} is basic in row {context.Tableau.RowLabels[row]}.");
        text.AppendLine($"  Old coefficient = {Num.Format(oldCoefficient)}");
        text.AppendLine($"  New coefficient = {Num.Format(newCoefficient)}   (change of {Num.Format(delta)})");
        text.AppendLine($"  The z row becomes: old z row + ({Num.Format(delta)} x row {context.Tableau.RowLabels[row]}).");
        text.AppendLine($"  New z = {Num.Format(values[Tableau.ObjectiveRow, context.Tableau.RhsColumn])}");
        text.AppendLine();
        text.Append(context.ReOptimise(changed, out _));

        return text.ToString();
    }
}

/// <summary>
/// Brief: "Apply and display a change of a selected constraint right-hand-side value."
/// b* = B-inverse * b is recomputed, and a negative entry sends the table to the Dual Simplex.
/// </summary>
public sealed class RhsChangeOperation : SensitivityOperation
{
    public override string Name => "Change a constraint right-hand-side";

    public override string Description =>
        "Apply a new right-hand-side value and re-optimise.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        int row = PickRow(context, input, "Which constraint?");
        if (row < 0)
        {
            return Cancelled;
        }

        double oldRhs = context.OriginalRhs(row);
        if (!input.TryReadNumber(
                $"New right-hand-side for {context.DescribeRow(row)} (currently {Num.Format(oldRhs)}): ",
                out double newRhs))
        {
            return Cancelled;
        }

        bool negated = context.Form.Rows[row].Negated;
        double delta = (newRhs - oldRhs) * (negated ? -1.0 : 1.0);

        double[,] values = context.Tableau.CopyValues();
        int rhsColumn = context.Tableau.RhsColumn;

        // Every basic value moves by delta times column i of B-inverse, and z moves by the shadow price.
        for (int r = 0; r < context.ConstraintCount; r++)
        {
            values[r + Tableau.FirstConstraintRow, rhsColumn] += delta * context.Inverse[r, row];
        }

        values[Tableau.ObjectiveRow, rhsColumn] += delta * context.ShadowPrices[row];

        Tableau changed = context.Tableau.WithValues(values, context.Tableau.Label + "*");

        var text = new StringBuilder();
        text.Append(context.Header($"Change the right-hand-side of {context.DescribeRow(row)}"));
        text.AppendLine($"  Old right-hand-side = {Num.Format(oldRhs)}");
        text.AppendLine($"  New right-hand-side = {Num.Format(newRhs)}");
        text.AppendLine($"  Shadow price        = {Num.Format(context.ConstraintShadowPrices[row])}");
        text.AppendLine($"  New b* = B-inverse x b, and z moves by (shadow price x change) to " +
                        $"{Num.Format(values[Tableau.ObjectiveRow, rhsColumn])}.");
        text.AppendLine();

        if (changed.HasNegativeRightHandSide())
        {
            text.AppendLine("  A basic variable went negative, so the table is no longer feasible and the " +
                            "Dual Simplex runs to fix it.");
            text.AppendLine();
        }

        text.Append(context.ReOptimise(changed, out _));

        return text.ToString();
    }
}

/// <summary>
/// Brief: "Apply and display a change of a selected variable in a Non-Basic Variable column."
/// The column is re-priced: its table entries become B-inverse * A_j and its z row value becomes
/// CBV * B-inverse * A_j - c_j.
/// </summary>
public sealed class NonBasicColumnCoefficientChangeOperation : SensitivityOperation
{
    public override string Name => "Change a coefficient in a Non-Basic Variable column";

    public override string Description =>
        "Apply a new technological coefficient inside a non-basic column and re-optimise.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        int column = PickNonBasicColumn(context, input, "Which non-basic column?");
        if (column < 0)
        {
            return Cancelled;
        }

        int row = PickRow(context, input, "Which constraint's coefficient inside that column?");
        if (row < 0)
        {
            return Cancelled;
        }

        bool negated = context.Form.Rows[row].Negated;
        double oldCoefficient = context.Form.InitialTableau[row + Tableau.FirstConstraintRow, column] * (negated ? -1.0 : 1.0);

        if (!input.TryReadNumber(
                $"New coefficient for {context.ColumnName(column)} in {context.DescribeRow(row)} " +
                $"(currently {Num.Format(oldCoefficient)}): ",
                out double newCoefficient))
        {
            return Cancelled;
        }

        // Rebuild the original column with the new value, then price it out against the current basis.
        double[] original = BasisMatrix.OriginalColumn(context.Form, column);
        original[row] = newCoefficient * (negated ? -1.0 : 1.0);

        double[] priced = BasisMatrix.Multiply(context.Inverse, original);
        double reducedCost = BasisMatrix.Dot(context.ShadowPrices, original) - context.ObjectiveCoefficients[column];

        double[,] values = context.Tableau.CopyValues();
        values[Tableau.ObjectiveRow, column] = reducedCost;
        for (int r = 0; r < context.ConstraintCount; r++)
        {
            values[r + Tableau.FirstConstraintRow, column] = priced[r];
        }

        Tableau changed = context.Tableau.WithValues(values, context.Tableau.Label + "*");

        var text = new StringBuilder();
        text.Append(context.Header($"Change the coefficient of {context.ColumnName(column)} in {context.DescribeRow(row)}"));
        text.AppendLine($"  Old coefficient = {Num.Format(oldCoefficient)}");
        text.AppendLine($"  New coefficient = {Num.Format(newCoefficient)}");
        text.AppendLine($"  New column      = B-inverse x A_j = [{string.Join(", ", priced.Select(Num.Format))}]");
        text.AppendLine($"  New c_j*        = {Num.Format(reducedCost)}");
        text.AppendLine();
        text.Append(context.ReOptimise(changed, out _));

        return text.ToString();
    }
}
