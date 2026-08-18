using System.Text;

namespace LPR381.Core.Models;

/// <summary>
/// The Linear Programming / Integer Programming model as it was entered in the input file.
/// This is deliberately *not* a canonical form - the brief requires the raw model to be the input,
/// and each algorithm builds its own canonical form from this.
/// </summary>
public sealed class LinearModel
{
    public LinearModel(ObjectiveSense sense, IEnumerable<Variable> variables, IEnumerable<Constraint> constraints)
    {
        Sense = sense;
        Variables = variables.ToArray();
        Constraints = constraints.ToArray();

        if (Variables.Count == 0)
        {
            throw new ArgumentException("A model needs at least one decision variable.", nameof(variables));
        }

        foreach (Constraint constraint in Constraints)
        {
            if (constraint.Coefficients.Count != Variables.Count)
            {
                throw new ArgumentException(
                    $"Constraint '{constraint.Name}' has {constraint.Coefficients.Count} coefficients " +
                    $"but the model has {Variables.Count} decision variables.",
                    nameof(constraints));
            }
        }
    }

    public ObjectiveSense Sense { get; }

    public IReadOnlyList<Variable> Variables { get; }

    public IReadOnlyList<Constraint> Constraints { get; }

    /// <summary>True when at least one variable is int or bin, i.e. this is an Integer Programming model.</summary>
    public bool IsIntegerModel => Variables.Any(v => v.IsDiscrete);

    /// <summary>True when every variable is binary - the shape the Knapsack algorithm expects.</summary>
    public bool IsBinaryModel => Variables.All(v => v.SignRestriction == SignRestriction.Binary);

    /// <summary>
    /// True when any objective term carries a power other than 1, e.g. <c>max +1^2 +3</c> for
    /// <c>max z = x1^2 + 3x2</c>. Only the Non-Linear solver can handle one of these.
    /// </summary>
    public bool IsNonLinear => Variables.Any(v => v.IsNonLinear);

    /// <summary>Renders the model back out in readable maths, for the screen and the output file.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(Sense == ObjectiveSense.Max ? "max z = " : "min z = ");
        text.AppendLine(FormatTerms(Variables.Select(v => v.ObjectiveCoefficient).ToArray(), isObjective: true));

        for (int i = 0; i < Constraints.Count; i++)
        {
            Constraint constraint = Constraints[i];
            text.Append(i == 0 ? "s.t.    " : "        ");
            text.Append(FormatTerms(constraint.Coefficients));
            text.AppendLine($" {constraint.RelationSymbol} {Num.Format(constraint.RightHandSide)}");
        }

        text.Append("        ");
        text.Append(string.Join(", ", Variables.Select(v => $"{v.Name} {DescribeSign(v.SignRestriction)}")));
        return text.ToString();
    }

    private string FormatTerms(IReadOnlyList<double> coefficients, bool isObjective = false)
    {
        var text = new StringBuilder();

        for (int i = 0; i < coefficients.Count; i++)
        {
            double coefficient = coefficients[i];
            if (i > 0)
            {
                text.Append(Num.IsNegative(coefficient) ? " - " : " + ");
            }
            else if (Num.IsNegative(coefficient))
            {
                text.Append('-');
            }

            text.Append(Num.Format(Math.Abs(coefficient)));
            text.Append(Variables[i].Name);

            // A non-linear objective term prints its power, e.g. 2.000x1^2.
            if (isObjective && Variables[i].IsNonLinear)
            {
                text.Append('^');
                text.Append(Num.Format(Variables[i].ObjectivePower).TrimEnd('0').TrimEnd('.'));
            }
        }

        return text.ToString();
    }

    private static string DescribeSign(SignRestriction restriction) => restriction switch
    {
        SignRestriction.Positive => ">= 0",
        SignRestriction.Negative => "<= 0",
        SignRestriction.Unrestricted => "urs",
        SignRestriction.Integer => ">= 0 and integer",
        SignRestriction.Binary => "= 0 or 1",
        _ => string.Empty
    };
}
