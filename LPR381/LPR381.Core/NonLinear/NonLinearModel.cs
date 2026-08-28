using System.Text;
using LPR381.Core.Models;

namespace LPR381.Core.NonLinear;

/// <summary>
/// A non-linear programming model of the kind the Golden Section Search solves (study notes section
/// 18): one function of one variable, to be maximised or minimised over a closed interval.
/// <para>
/// This is deliberately not a <see cref="LinearModel"/>. There is no objective row, no constraints to
/// turn into slack variables and no tableau - the search only ever evaluates f at points inside
/// [<see cref="Lower"/>, <see cref="Upper"/>], so those three things plus the tolerance are the whole
/// model.
/// </para>
/// </summary>
public sealed class NonLinearModel
{
    public NonLinearModel(
        ObjectiveSense sense,
        FunctionExpression function,
        double lower,
        double upper,
        double tolerance)
    {
        ArgumentNullException.ThrowIfNull(function);

        if (!(upper > lower))
        {
            throw new ArgumentException(
                $"The interval is empty: the upper bound ({Num.Format(upper)}) must be above the " +
                $"lower bound ({Num.Format(lower)}).");
        }

        if (!Num.IsPositive(tolerance))
        {
            throw new ArgumentException($"The tolerance must be above zero, but was {Num.Format(tolerance)}.");
        }

        Sense = sense;
        Function = function;
        Lower = lower;
        Upper = upper;
        Tolerance = tolerance;
    }

    public ObjectiveSense Sense { get; }

    public FunctionExpression Function { get; }

    /// <summary>The left hand end of the search interval, x_lower in the notes.</summary>
    public double Lower { get; }

    /// <summary>The right hand end of the search interval, x_upper in the notes.</summary>
    public double Upper { get; }

    /// <summary>The search stops once the interval is narrower than this - the notes' epsilon.</summary>
    public double Tolerance { get; }

    public string VariableName => Function.VariableName;

    /// <summary>The model written out the way it was read, for the screen and the output file.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        string sense = Sense == ObjectiveSense.Max ? "max" : "min";

        text.AppendLine($"{sense} f({VariableName}) = {Function.Text}");
        text.AppendLine($"s.t.    {Num.Format(Lower)} <= {VariableName} <= {Num.Format(Upper)}");
        text.Append($"        stop when the interval is narrower than {Num.Format(Tolerance)}");

        return text.ToString();
    }
}
