using System.Text;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Sensitivity;

/// <summary>
/// Brief: "Add a new activity to an optimal solution."
/// A new decision variable is priced out against the current basis. If it does not pay, the answer
/// does not change; if it does, it enters and the simplex carries on from the table that is already there.
/// </summary>
public sealed class AddActivityOperation : SensitivityOperation
{
    public override string Name => "Add a new activity";

    public override string Description =>
        "Add a new decision variable to the optimal solution and price it out.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        string name = "x" + (context.Model.Variables.Count + 1);

        if (!input.TryReadNumber($"Objective coefficient for the new activity {name}: ", out double objective))
        {
            return Cancelled;
        }

        double[] column = new double[context.ConstraintCount];
        for (int row = 0; row < context.ConstraintCount; row++)
        {
            if (!input.TryReadNumber($"Coefficient of {name} in {context.DescribeRow(row)}: ", out column[row]))
            {
                return Cancelled;
            }
        }

        Tableau extended = TableauOperations.AddActivity(
            context.Tableau, column, objective, context.Form, name, context.Tableau.Label + "*");

        double reducedCost = extended[Tableau.ObjectiveRow, extended.RhsColumn - 1];
        bool worthwhile = context.Sense == ObjectiveSense.Max ? Num.IsNegative(reducedCost) : Num.IsPositive(reducedCost);

        var text = new StringBuilder();
        text.Append(context.Header($"Add a new activity {name}"));
        text.AppendLine($"  Objective coefficient : {Num.Format(objective)}");
        text.AppendLine($"  Column                : [{string.Join(", ", column.Select(Num.Format))}]");
        text.AppendLine($"  Shadow prices         : [{string.Join(", ", context.ConstraintShadowPrices.Select(Num.Format))}]");
        text.AppendLine($"  Priced out c* = (CBV x B-inverse x A_new) - c = {Num.Format(reducedCost)}");
        text.AppendLine();
        text.AppendLine(worthwhile
            ? $"  {name} prices out as attractive, so it enters the basis and the simplex carries on."
            : $"  {name} does not pay at the current shadow prices, so the optimal solution does not change.");
        text.AppendLine();
        text.Append(context.ReOptimise(extended, out _));

        return text.ToString();
    }
}

/// <summary>
/// Brief: "Add a new constraint to an optimal solution."
/// The row is appended and put back into canonical form against the current basis - the same "fix the
/// row" step Branch &amp; Bound uses - then the Dual Simplex repairs any negative RHS.
/// </summary>
public sealed class AddConstraintOperation : SensitivityOperation
{
    public override string Name => "Add a new constraint";

    public override string Description =>
        "Add a constraint to the optimal solution and re-optimise with the Dual Simplex.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        double[] perVariable = new double[context.Model.Variables.Count];
        for (int i = 0; i < perVariable.Length; i++)
        {
            if (!input.TryReadNumber($"Coefficient of {context.Model.Variables[i].Name}: ", out perVariable[i]))
            {
                return Cancelled;
            }
        }

        int relationChoice = input.ReadChoice("Relation?", ["<=", ">="]);
        if (relationChoice < 0)
        {
            return Cancelled;
        }

        Relation relation = relationChoice == 0 ? Relation.LessOrEqual : Relation.GreaterOrEqual;

        if (!input.TryReadNumber("Right-hand-side: ", out double rhs))
        {
            return Cancelled;
        }

        double[] coefficients = context.ExpandDecisionCoefficients(perVariable);

        Tableau extended = TableauOperations.AddConstraint(
            context.Tableau,
            coefficients,
            relation,
            rhs,
            "new",
            IntegerRules.NextLogicalName(context.Tableau, context.Form.DecisionColumnCount, relation == Relation.LessOrEqual),
            context.Tableau.Label + "*");

        IReadOnlyDictionary<string, double> current = context.Form.ReadSolution(context.Tableau);
        double leftHandSide = 0.0;
        for (int i = 0; i < perVariable.Length; i++)
        {
            leftHandSide += perVariable[i] * current[context.Model.Variables[i].Name];
        }

        bool alreadySatisfied = relation == Relation.LessOrEqual
            ? leftHandSide <= rhs + Num.Epsilon
            : leftHandSide >= rhs - Num.Epsilon;

        var text = new StringBuilder();
        text.Append(context.Header("Add a new constraint"));
        text.Append("  New constraint : ");
        text.AppendLine(string.Join(" + ", context.Model.Variables.Select((v, i) => $"{Num.Format(perVariable[i])}{v.Name}")) +
                        $" {(relation == Relation.LessOrEqual ? "<=" : ">=")} {Num.Format(rhs)}");
        text.AppendLine($"  At the current solution the left-hand-side is {Num.Format(leftHandSide)}.");
        text.AppendLine();
        text.AppendLine(alreadySatisfied
            ? "  The current solution already satisfies it, so the answer does not change - the new slack " +
              "just comes in basic."
            : "  The current solution breaks it, so the new row arrives with a negative RHS and the Dual " +
              "Simplex has to repair the table.");
        text.AppendLine();
        text.Append(context.ReOptimise(extended, out _));

        return text.ToString();
    }
}
