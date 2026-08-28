using System.Globalization;
using System.Text;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.NonLinear;

namespace LPR381.Core.Algorithms;

/// <summary>
/// The Golden Section Search, study notes section 18 - the bonus criterion's non-linear algorithm.
/// <para>
/// <b>What it is for.</b> A numerical search for the optimum of a <i>unimodal</i> function of one
/// variable (exactly one peak or one trough on the interval). It never differentiates the function and
/// never builds a tableau; it only ever asks "what is f at this point?", which is why it copes with
/// things like <c>4sin(x)(1 + cos(x))</c> that no simplex method can touch.
/// </para>
/// <para>
/// <b>The idea.</b> Two interior points are placed in the interval. Comparing f at them tells you which
/// end of the interval cannot contain the optimum, so that end is thrown away and the interval shrinks.
/// The points are placed using the golden ratio conjugate <c>0.618...</c> rather than at the thirds,
/// because that spacing makes one of this iteration's points land exactly on one of next iteration's
/// points - so every iteration after the first needs only ONE new evaluation of f, not two.
/// </para>
/// <para>
/// <b>Each iteration</b> (notes section 18): <c>d = 0.618(x_upper - x_lower)</c>,
/// <c>X1 = x_lower + d</c>, <c>X2 = x_upper - d</c>. For a maximum, if <c>f(X2) &gt; f(X1)</c> the peak
/// lies left of X1, so X1 becomes the new upper bound; otherwise X2 becomes the new lower bound. For a
/// minimum the comparison flips. Stop once <c>x_upper - x_lower &lt; epsilon</c> and answer with the
/// midpoint.
/// </para>
/// </summary>
public sealed class GoldenSectionSolver
{
    /// <summary>
    /// The golden ratio conjugate, (sqrt(5) - 1) / 2. The notes round it to 0.618; the full value is
    /// used here so the numbers match a spreadsheet worked to full precision.
    /// </summary>
    public const double Conjugate = 0.6180339887498949;

    /// <summary>Stops a runaway search if the tolerance is impossibly small for double precision.</summary>
    public const int MaxIterations = 200;

    /// <summary>
    /// The search table is where the interval shrinking has to be visible, and 3 decimals hides that,
    /// so this one table is shown to 6. The final answer still reports at the usual 3.
    /// </summary>
    private const string SearchFormat = "0.000000";

    public string Name => "Golden Section Search";

    public string Description =>
        "Finds the optimum of a one-variable non-linear function by repeatedly shrinking the interval " +
        "using the golden ratio (study notes section 18).";

    public SolveResult Solve(NonLinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<IterationStep> steps = [];
        steps.Add(MethodStep(model));

        double lower = model.Lower;
        double upper = model.Upper;
        bool maximising = model.Sense == ObjectiveSense.Max;

        // Rows of the running table, so the whole search can be shown again at the end the way the
        // notes lay it out.
        List<IReadOnlyList<string>> tableRows = [];

        // The point that survives an iteration keeps its function value, so it does not have to be
        // worked out again. These carry it across.
        (double Point, double Value)? knownX1 = null;
        (double Point, double Value)? knownX2 = null;
        int evaluations = 0;
        int iteration = 0;

        try
        {
            while (upper - lower >= model.Tolerance)
            {
                if (++iteration > MaxIterations)
                {
                    return SolveResult.Failed(
                        Name,
                        SolutionStatus.Error,
                        $"The interval was still wider than {Num.Format(model.Tolerance)} after {MaxIterations} " +
                        "iterations. Use a larger tolerance.",
                        null,
                        steps);
                }

                double d = Conjugate * (upper - lower);
                double x1 = lower + d;
                double x2 = upper - d;

                bool reusedX1 = knownX1 is { } carriedOne && Num.AreEqual(carriedOne.Point, x1);
                bool reusedX2 = knownX2 is { } carriedTwo && Num.AreEqual(carriedTwo.Point, x2);

                double f1 = reusedX1 ? knownX1!.Value.Value : Evaluate(model, x1, ref evaluations);
                double f2 = reusedX2 ? knownX2!.Value.Value : Evaluate(model, x2, ref evaluations);

                tableRows.Add([
                    iteration.ToString(CultureInfo.InvariantCulture),
                    Format(lower), Format(upper), Format(d),
                    Format(x1), Format(x2), Format(f1), Format(f2)
                ]);

                // The comparison from the notes. For a maximum, the larger f marks the side worth
                // keeping; for a minimum it is the smaller one.
                bool keepLeft = maximising ? f2 > f1 : f2 < f1;

                steps.Add(IterationStep.Text(
                    $"Iteration {iteration}",
                    OneIteration(model, lower, upper, d, x1, x2, f1, f2, reusedX1, reusedX2),
                    Decision(model, x1, x2, f1, f2, keepLeft)));

                if (keepLeft)
                {
                    // The optimum is left of X1, so X1 is the new upper bound. This iteration's X2
                    // becomes next iteration's X1 - that is the saving the golden ratio buys.
                    upper = x1;
                    knownX1 = (x2, f2);
                    knownX2 = null;
                }
                else
                {
                    // The optimum is right of X2, so X2 is the new lower bound, and this iteration's
                    // X1 becomes next iteration's X2.
                    lower = x2;
                    knownX2 = (x1, f1);
                    knownX1 = null;
                }
            }

            double best = (lower + upper) / 2.0;
            double value = Evaluate(model, best, ref evaluations);

            steps.Add(IterationStep.Text(
                "The search table",
                MatrixFormatter.Table(
                    ["iter", "x_lower", "x_upper", "d", "X1", "X2", "f(X1)", "f(X2)"],
                    tableRows),
                $"{iteration} iteration(s), {evaluations} evaluation(s) of f. Two points are needed to start, " +
                "then only one more per iteration, because the surviving point keeps its value."));

            steps.Add(IterationStep.Text("Answer", Answer(model, lower, upper, best, value),
                $"The interval is now {Format(upper - lower)} wide, which is under the tolerance of " +
                $"{Format(model.Tolerance)}, so the search stops and the midpoint is the answer."));

            return SolveResult.OptimalWithoutTableau(
                Name,
                steps,
                value,
                new Dictionary<string, double> { [model.VariableName] = best },
                $"Optimum found by Golden Section Search after {iteration} iteration(s), to within " +
                $"{Num.Format(model.Tolerance)} on {model.VariableName}.");
        }
        catch (FunctionParseException error)
        {
            return SolveResult.Failed(Name, SolutionStatus.Error, error.Message, null, steps);
        }
    }

    private static double Evaluate(NonLinearModel model, double x, ref int evaluations)
    {
        evaluations++;
        return model.Function.Evaluate(x);
    }

    private static IterationStep MethodStep(NonLinearModel model)
    {
        var text = new StringBuilder();

        text.AppendLine("  The Golden Section Search shrinks the interval that the optimum is known to lie in.");
        text.AppendLine("  It only ever evaluates f, so the function can be anything - no derivative is needed.");
        text.AppendLine();
        text.AppendLine("  Each iteration:");
        text.AppendLine($"    d  = {Conjugate.ToString(SearchFormat, CultureInfo.InvariantCulture)} x (x_upper - x_lower)");
        text.AppendLine("    X1 = x_lower + d");
        text.AppendLine("    X2 = x_upper - d");
        text.AppendLine();

        if (model.Sense == ObjectiveSense.Max)
        {
            text.AppendLine("  Maximising, so the larger f marks the side to keep:");
            text.AppendLine("    f(X2) > f(X1)  ->  the peak is left of X1,  X1 becomes the new x_upper");
            text.AppendLine("    f(X2) < f(X1)  ->  the peak is right of X2, X2 becomes the new x_lower");
        }
        else
        {
            text.AppendLine("  Minimising, so the smaller f marks the side to keep:");
            text.AppendLine("    f(X2) < f(X1)  ->  the trough is left of X1,  X1 becomes the new x_upper");
            text.AppendLine("    f(X2) > f(X1)  ->  the trough is right of X2, X2 becomes the new x_lower");
        }

        text.AppendLine();
        text.Append($"  Stop once x_upper - x_lower < {Format(model.Tolerance)}, then answer with the midpoint.");

        return IterationStep.Text("Method - Golden Section Search (study notes section 18)", text.ToString(),
            $"f({model.VariableName}) = {model.Function.Text} is searched on " +
            $"[{Format(model.Lower)}, {Format(model.Upper)}]. The method assumes it is unimodal there - " +
            "exactly one peak or trough.");
    }

    private static string OneIteration(
        NonLinearModel model,
        double lower,
        double upper,
        double d,
        double x1,
        double x2,
        double f1,
        double f2,
        bool reusedX1,
        bool reusedX2)
    {
        string name = model.VariableName;
        var text = new StringBuilder();

        text.AppendLine($"    x_lower = {Format(lower)}      x_upper = {Format(upper)}");
        text.AppendLine($"    d  = {Format(Conjugate)} x ({Format(upper)} - {Format(lower)}) = {Format(d)}");
        text.AppendLine($"    X1 = {Format(lower)} + {Format(d)} = {Format(x1)}");
        text.AppendLine($"    X2 = {Format(upper)} - {Format(d)} = {Format(x2)}");
        text.AppendLine();
        text.AppendLine($"    f(X1) = f({Format(x1)}) = {Format(f1)}{(reusedX1 ? "   (carried over)" : string.Empty)}");
        text.Append($"    f(X2) = f({Format(x2)}) = {Format(f2)}{(reusedX2 ? "   (carried over)" : string.Empty)}");

        if (reusedX1 || reusedX2)
        {
            text.AppendLine();
            text.Append($"    Only one new value of f was needed, because a point from the previous " +
                        $"interval landed here again ({name} is placed by the golden ratio).");
        }

        return text.ToString();
    }

    private static string Decision(
        NonLinearModel model,
        double x1,
        double x2,
        double f1,
        double f2,
        bool keepLeft)
    {
        string comparison = Num.AreEqual(f1, f2)
            ? $"f(X1) = f(X2) = {Format(f1)}"
            : f2 > f1
                ? $"f(X2) = {Format(f2)} > f(X1) = {Format(f1)}"
                : $"f(X2) = {Format(f2)} < f(X1) = {Format(f1)}";

        string target = model.Sense == ObjectiveSense.Max ? "peak" : "trough";

        return keepLeft
            ? $"{comparison}, so the {target} cannot be right of X1. Drop the old x_upper: " +
              $"X1 = {Format(x1)} becomes the new x_upper."
            : $"{comparison}, so the {target} cannot be left of X2. Drop the old x_lower: " +
              $"X2 = {Format(x2)} becomes the new x_lower.";
    }

    private static string Answer(NonLinearModel model, double lower, double upper, double best, double value)
    {
        string name = model.VariableName;
        var text = new StringBuilder();

        text.AppendLine($"    x_lower = {Format(lower)}      x_upper = {Format(upper)}");
        text.AppendLine($"    {name}  = ({Format(lower)} + {Format(upper)}) / 2 = {Format(best)}");
        text.Append($"    f({name}) = {Format(value)}");

        return text.ToString();
    }

    private static string Format(double value) => value.ToString(SearchFormat, CultureInfo.InvariantCulture);
}
