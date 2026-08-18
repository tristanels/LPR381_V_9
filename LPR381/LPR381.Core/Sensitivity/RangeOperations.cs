using System.Text;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Sensitivity;

/// <summary>
/// Brief: "Display the range of a selected Non-Basic Variable."
/// A non-basic variable is out of the solution because bringing it in would not pay. Its objective
/// coefficient can move by its reduced cost before that stops being true.
/// </summary>
public sealed class NonBasicVariableRangeOperation : SensitivityOperation
{
    public override string Name => "Range of a Non-Basic Variable";

    public override string Description =>
        "How far the objective coefficient of a non-basic variable may move before it would enter the basis.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        int column = PickNonBasicColumn(context, input, "Which non-basic variable?");
        if (column < 0)
        {
            return Cancelled;
        }

        double reducedCost = context.Tableau[Tableau.ObjectiveRow, column];
        double coefficient = context.ObjectiveCoefficients[column];

        // c_j moves by delta, so the z row value moves by -delta. It must keep its optimal sign.
        (double decrease, double increase) = AllowableChange([(reducedCost, -1.0)], context.Sense);

        var text = new StringBuilder();
        text.Append(context.Header($"Range of the non-basic variable {context.ColumnName(column)}"));
        text.AppendLine($"  {context.ColumnName(column)} is non-basic, so its value is 0.");
        text.AppendLine($"  Objective coefficient c   = {Num.Format(coefficient)}");
        text.AppendLine($"  Reduced cost (z row)      = {Num.Format(reducedCost)}");
        text.AppendLine();
        text.AppendLine($"  Allowable increase        = {(double.IsInfinity(increase) ? "infinite" : Num.Format(increase))}");
        text.AppendLine($"  Allowable decrease        = {(double.IsInfinity(decrease) ? "infinite" : Num.Format(decrease))}");
        text.AppendLine($"  Range for c               = {SensitivityContext.FormatRange(coefficient - decrease, coefficient + increase)}");
        text.AppendLine();
        text.AppendLine(
            "  Inside this range the current basis stays optimal and the solution does not change. " +
            $"Past it, {context.ColumnName(column)} becomes worth bringing into the basis.");

        return text.ToString();
    }
}

/// <summary>
/// Brief: "Display the range of a selected Basic Variable."
/// A basic variable is in the solution. Its coefficient can move until one of the non-basic columns
/// would price out as attractive, which is a ratio test across that variable's row.
/// </summary>
public sealed class BasicVariableRangeOperation : SensitivityOperation
{
    public override string Name => "Range of a Basic Variable";

    public override string Description =>
        "How far the objective coefficient of a basic variable may move before the basis changes.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        int column = PickBasicColumn(context, input, "Which basic variable?");
        if (column < 0)
        {
            return Cancelled;
        }

        int row = context.RowOf(column);
        double coefficient = context.ObjectiveCoefficients[column];

        // Changing this basic cost by delta moves every non-basic z row value by delta * a(row, j).
        List<(double Current, double Rate)> limits = [];
        List<IReadOnlyList<string>> working = [];

        foreach (int other in context.NonBasicColumns)
        {
            double reducedCost = context.Tableau[Tableau.ObjectiveRow, other];
            double rate = context.Tableau[row, other];
            limits.Add((reducedCost, rate));

            working.Add(
            [
                context.ColumnName(other),
                Num.Format(reducedCost),
                Num.Format(rate),
                Num.IsZero(rate) ? "no limit" : Num.Format(-reducedCost / rate)
            ]);
        }

        (double decrease, double increase) = AllowableChange(limits, context.Sense);

        var text = new StringBuilder();
        text.Append(context.Header($"Range of the basic variable {context.ColumnName(column)}"));
        text.AppendLine($"  {context.ColumnName(column)} is basic in row {context.Tableau.RowLabels[row]} " +
                        $"with value {Num.Format(context.Tableau.Rhs(row))}.");
        text.AppendLine($"  Objective coefficient c = {Num.Format(coefficient)}");
        text.AppendLine();
        text.AppendLine("  Ratio test across that row against the z row:");
        text.Append(MatrixFormatter.Table(["non-basic column", "z row", "row value", "delta limit"], working));
        text.AppendLine();
        text.AppendLine($"  Allowable increase      = {(double.IsInfinity(increase) ? "infinite" : Num.Format(increase))}");
        text.AppendLine($"  Allowable decrease      = {(double.IsInfinity(decrease) ? "infinite" : Num.Format(decrease))}");
        text.AppendLine($"  Range for c             = {SensitivityContext.FormatRange(coefficient - decrease, coefficient + increase)}");
        text.AppendLine();
        text.AppendLine(
            "  Inside this range the same variables stay basic. The values of the variables do not change, " +
            "but z does, because this variable is in the solution.");

        return text.ToString();
    }
}

/// <summary>
/// Brief: "Display the range of a selected constraint right-hand-side value."
/// This is the range over which the shadow price stays valid.
/// </summary>
public sealed class RhsRangeOperation : SensitivityOperation
{
    public override string Name => "Range of a constraint right-hand-side";

    public override string Description =>
        "How far a right-hand-side may move while the current basis, and its shadow price, stay valid.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        int row = PickRow(context, input, "Which constraint?");
        if (row < 0)
        {
            return Cancelled;
        }

        bool negated = context.Form.Rows[row].Negated;
        double rhs = context.OriginalRhs(row);

        // b* = B-inverse * b. Changing b_i by delta moves b* by delta * (column i of B-inverse), and
        // every entry must stay non-negative for the basis to remain feasible.
        List<(double Current, double Rate)> limits = [];
        List<IReadOnlyList<string>> working = [];

        for (int r = 0; r < context.ConstraintCount; r++)
        {
            double current = context.Tableau.Rhs(r + Tableau.FirstConstraintRow);
            double rate = context.Inverse[r, row] * (negated ? -1.0 : 1.0);
            limits.Add((current, rate));

            working.Add(
            [
                BasisMatrix.BasicNames(context.Tableau)[r],
                Num.Format(current),
                Num.Format(rate),
                Num.IsZero(rate) ? "no limit" : Num.Format(-current / rate)
            ]);
        }

        // Feasibility is a non-negativity test in both max and min problems.
        (double decrease, double increase) = AllowableChange(limits, ObjectiveSense.Max);

        var text = new StringBuilder();
        text.Append(context.Header($"Range of the right-hand-side of {context.DescribeRow(row)}"));
        text.AppendLine($"  Current right-hand-side = {Num.Format(rhs)}");
        text.AppendLine($"  Shadow price            = {Num.Format(context.ConstraintShadowPrices[row])}");
        text.AppendLine();
        text.AppendLine("  b* = B-inverse x b must stay non-negative:");
        text.Append(MatrixFormatter.Table(["basic variable", "b*", "B-inverse column", "delta limit"], working));
        text.AppendLine();
        text.AppendLine($"  Allowable increase      = {(double.IsInfinity(increase) ? "infinite" : Num.Format(increase))}");
        text.AppendLine($"  Allowable decrease      = {(double.IsInfinity(decrease) ? "infinite" : Num.Format(decrease))}");
        text.AppendLine($"  Range for the RHS       = {SensitivityContext.FormatRange(rhs - decrease, rhs + increase)}");
        text.AppendLine();
        text.AppendLine(
            "  Inside this range the same variables stay basic and each extra unit is worth the shadow price. " +
            "Outside it the basis changes and the shadow price no longer applies.");

        return text.ToString();
    }
}

/// <summary>
/// Brief: "Display the range of a selected variable in a Non-Basic Variable column."
/// This is a technological coefficient inside a column that is currently out of the solution.
/// </summary>
public sealed class NonBasicColumnCoefficientRangeOperation : SensitivityOperation
{
    public override string Name => "Range of a coefficient in a Non-Basic Variable column";

    public override string Description =>
        "How far a technological coefficient inside a non-basic column may move before that column enters.";

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
        double coefficient = context.Form.InitialTableau[row + Tableau.FirstConstraintRow, column] * (negated ? -1.0 : 1.0);
        double reducedCost = context.Tableau[Tableau.ObjectiveRow, column];
        double shadowPrice = context.ShadowPrices[row] * (negated ? -1.0 : 1.0);

        // c_j* = y * A_j - c_j, so changing a(i,j) by delta moves the reduced cost by y_i * delta.
        (double decrease, double increase) = AllowableChange([(reducedCost, shadowPrice)], context.Sense);

        var text = new StringBuilder();
        text.Append(context.Header($"Range of the coefficient of {context.ColumnName(column)} in {context.DescribeRow(row)}"));
        text.AppendLine($"  Current coefficient    = {Num.Format(coefficient)}");
        text.AppendLine($"  Reduced cost of {context.ColumnName(column),-6} = {Num.Format(reducedCost)}");
        text.AppendLine($"  Shadow price of the row = {Num.Format(context.ConstraintShadowPrices[row])}");
        text.AppendLine();
        text.AppendLine("  c_j* = (CBV x B-inverse x A_j) - c_j, so a change of delta moves c_j* by (shadow price x delta).");
        text.AppendLine();
        text.AppendLine($"  Allowable increase     = {(double.IsInfinity(increase) ? "infinite" : Num.Format(increase))}");
        text.AppendLine($"  Allowable decrease     = {(double.IsInfinity(decrease) ? "infinite" : Num.Format(decrease))}");
        text.AppendLine($"  Range                  = {SensitivityContext.FormatRange(coefficient - decrease, coefficient + increase)}");
        text.AppendLine();
        text.AppendLine(
            $"  Inside this range {context.ColumnName(column)} stays out of the solution. " +
            "Past it the column prices out as attractive and would enter the basis.");

        return text.ToString();
    }
}

/// <summary>
/// Brief: "Display the shadow prices." These are CBV * B-inverse, read straight off the z row under
/// the slack and excess columns of the optimal table.
/// </summary>
public sealed class ShadowPriceOperation : SensitivityOperation
{
    public override string Name => "Display the shadow prices";

    public override string Description =>
        "What one more unit of each constrained resource is worth.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        List<IReadOnlyList<string>> rows = [];

        for (int row = 0; row < context.ConstraintCount; row++)
        {
            CanonicalRow canonicalRow = context.Form.Rows[row];
            double price = context.ConstraintShadowPrices[row];
            double slack = context.Tableau.ReadColumnValues()[context.ColumnName(canonicalRow.LogicalColumn)];

            rows.Add(
            [
                canonicalRow.Label,
                canonicalRow.SourceConstraintIndex >= 0
                    ? context.Model.Constraints[canonicalRow.SourceConstraintIndex].Name
                    : "bound",
                Num.Format(context.OriginalRhs(row)),
                context.ColumnName(canonicalRow.LogicalColumn),
                Num.Format(slack),
                Num.Format(price),
                Num.IsZero(price) ? "not binding (slack left over)" : "binding"
            ]);
        }

        var text = new StringBuilder();
        text.Append(context.Header("Shadow prices"));
        text.AppendLine("  Shadow price = CBV x B-inverse, read off the z row under each row's slack or excess column.");
        text.AppendLine();
        text.Append(MatrixFormatter.Table(
            ["row", "constraint", "rhs", "slack/excess", "value", "shadow price", "status"], rows));
        text.AppendLine();
        text.AppendLine(
            "  A shadow price is what z would change by if that right-hand-side went up by one unit, " +
            "as long as the change stays inside the RHS range. A resource with slack left over is not " +
            "binding, so its shadow price is zero.");

        return text.ToString();
    }
}
