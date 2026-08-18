using System.Text;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Algorithms;

/// <summary>
/// The bonus criterion: "the ability to solve any non-linear problem like f(x)=x^2 with any algorithm".
/// <para>
/// <b>How it works.</b> A non-linear objective term such as <c>2x1^2</c> is <i>separable</i> - it
/// involves one variable on its own. A separable term can be approximated as closely as you like by a
/// piecewise linear function, and a piecewise linear function can be written as an ordinary LP. That
/// means the non-linear model is turned into a linear one and handed to the Primal Simplex that
/// already exists - "solved with any algorithm", with no new solver written.
/// </para>
/// <para>
/// <b>The lambda method.</b> For each non-linear variable the solver:
/// </para>
/// <list type="number">
/// <item>works out an upper bound U from the constraints (the notes' "finding M" rule: set the other
/// variables to zero, divide each RHS by that variable's coefficient, take the smallest);</item>
/// <item>puts breakpoints b0=0, b1, ... bK=U along [0, U] and works out f(bk) at each one;</item>
/// <item>introduces a weight lambda_k &gt;= 0 for every breakpoint, with sum(lambda_k) = 1;</item>
/// <item>replaces x by sum(bk * lambda_k) everywhere, and the non-linear objective term by
/// sum(f(bk) * lambda_k), which is linear in the lambdas.</item>
/// </list>
/// <para>
/// The resulting LP is solved by <see cref="PrimalSimplexSolver"/>, and x is read back out of the
/// lambda weights. The answer is exact at the breakpoints and interpolated between them, so more
/// segments means a closer approximation.
/// </para>
/// </summary>
public sealed class NonLinearSolver : ISolver
{
    /// <summary>Breakpoints per non-linear variable. More segments is more accurate but a wider table.</summary>
    public const int Segments = 5;

    public string Name => "Non-Linear (separable) Solver";

    public string Description =>
        "Solves a non-linear objective such as x1^2 by piecewise linear approximation, then the Primal Simplex.";

    public bool IsImplemented => true;

    public bool CanSolve(LinearModel model, out string reason)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!model.IsNonLinear)
        {
            reason =
                "This model is linear (no ^ power on any objective coefficient), so the ordinary " +
                "algorithms solve it exactly. Write a term like +2^2 to make it non-linear.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public SolveResult Solve(LinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!CanSolve(model, out string why))
        {
            return SolveResult.Failed(Name, SolutionStatus.Error, why);
        }

        List<IterationStep> steps = [];
        var explanation = new StringBuilder();

        explanation.AppendLine("  A non-linear term like 2x1^2 involves one variable on its own, so it is separable,");
        explanation.AppendLine("  and a separable function can be approximated by straight line segments. Each segment");
        explanation.AppendLine("  is linear, so the whole model becomes an ordinary LP that the Primal Simplex solves.");
        explanation.AppendLine();
        explanation.AppendLine("  For every non-linear variable x:");
        explanation.AppendLine("    1. find an upper bound U from the constraints;");
        explanation.AppendLine("    2. put breakpoints b0..bK along [0, U] and work out f(bk) at each;");
        explanation.AppendLine("    3. give every breakpoint a weight L_k >= 0 with sum(L_k) = 1;");
        explanation.AppendLine("    4. replace x by sum(bk * L_k) and f(x) by sum(f(bk) * L_k), which is linear.");
        steps.Add(IterationStep.Text("Method - piecewise linear (lambda) approximation", explanation.ToString(),
            $"{Segments} segment(s) per non-linear variable are used here."));

        // Step 1: bound every non-linear variable.
        Dictionary<int, double> bounds = [];
        foreach (int index in NonLinearIndices(model))
        {
            double bound = UpperBound(model, index);

            if (double.IsPositiveInfinity(bound))
            {
                string unbounded =
                    $"{model.Variables[index].Name} has no upper bound in the constraints, so the curve cannot be " +
                    "approximated over a finite range. Add a constraint that limits it.";
                return SolveResult.Failed(Name, SolutionStatus.Unbounded, unbounded, null, steps);
            }

            bounds[index] = bound;
        }

        // Steps 2 to 4: build the equivalent linear model.
        (LinearModel linear, List<BreakpointSet> breakpoints, List<int> linearIndices) = BuildLinearModel(model, bounds);

        foreach (BreakpointSet set in breakpoints)
        {
            List<IReadOnlyList<string>> rows = [];
            for (int k = 0; k < set.Points.Count; k++)
            {
                rows.Add([set.WeightNames[k], Num.Format(set.Points[k]), Num.Format(set.Values[k])]);
            }

            steps.Add(IterationStep.Text(
                $"Breakpoints for {set.VariableName}",
                $"  Upper bound U = {Num.Format(set.Points[^1])}, {Segments} segment(s).\n" +
                $"  f({set.VariableName}) = {Num.Format(set.Coefficient)} x {set.VariableName}^{Num.Format(set.Power)}\n\n" +
                MatrixFormatter.Table(["weight", "breakpoint", "f(breakpoint)"], rows),
                $"{set.VariableName} = " +
                string.Join(" + ", set.Points.Select((b, k) => $"{Num.Format(b)}{set.WeightNames[k]}")) +
                ", and the weights add up to 1."));
        }

        steps.Add(IterationStep.Text(
            "Equivalent linear model",
            "    " + linear.Describe().Replace(Environment.NewLine, Environment.NewLine + "    "),
            "This LP is what gets solved. Every lambda weight is a normal non-negative decision variable."));

        // The existing Primal Simplex does the actual solving - that is the point of the method.
        SolveResult inner = new PrimalSimplexSolver().Solve(linear);
        steps.AddRange(inner.Steps);

        if (!inner.IsOptimal)
        {
            return SolveResult.Failed(
                Name, inner.Status, "The approximating LP came out " + inner.Status + ": " + inner.Message,
                inner.CanonicalForm, steps);
        }

        // Read x back out of the weights, and report the true f(x) next to the approximation.
        Dictionary<string, double> solution = [];
        var recovery = new StringBuilder();
        List<IReadOnlyList<string>> recoveryRows = [];
        double trueObjective = 0.0;

        for (int i = 0; i < model.Variables.Count; i++)
        {
            Variable variable = model.Variables[i];
            double value;

            if (variable.IsNonLinear)
            {
                BreakpointSet set = breakpoints.First(s => s.VariableIndex == i);
                value = 0.0;
                for (int k = 0; k < set.Points.Count; k++)
                {
                    value += set.Points[k] * inner.VariableValues![set.WeightNames[k]];
                }
            }
            else
            {
                value = inner.VariableValues![linear.Variables[linearIndices[i]].Name];
            }

            solution[variable.Name] = value;

            double term = variable.ObjectiveCoefficient * Math.Pow(value, variable.ObjectivePower);
            trueObjective += term;

            recoveryRows.Add(
            [
                variable.Name,
                Num.Format(value),
                variable.IsNonLinear ? $"^{Num.Format(variable.ObjectivePower)}" : "linear",
                Num.Format(term)
            ]);
        }

        recovery.AppendLine("  x is read back out of the weights: x = sum(breakpoint x weight).");
        recovery.AppendLine();
        recovery.Append(MatrixFormatter.Table(["variable", "value", "term", "contribution to z"], recoveryRows));
        recovery.AppendLine();
        recovery.AppendLine($"  z from the approximating LP = {Num.Format(inner.ObjectiveValue)}");
        recovery.AppendLine($"  z from the true f(x)        = {Num.Format(trueObjective)}");

        double error = Math.Abs(trueObjective - inner.ObjectiveValue);
        steps.Add(IterationStep.Text("Recovering the answer", recovery.ToString(),
            Num.IsZero(error)
                ? "The optimum landed on a breakpoint, so the approximation is exact here."
                : $"The two differ by {Num.Format(error)} because the optimum falls between breakpoints. " +
                  "Raising the number of segments narrows that gap."));

        return SolveResult.Optimal(
            Name,
            inner.CanonicalForm!,
            steps,
            trueObjective,
            solution,
            $"Solved by piecewise linear approximation with {Segments} segment(s) per non-linear variable.");
    }

    /// <summary>The breakpoints and weights standing in for one non-linear variable.</summary>
    private sealed record BreakpointSet(
        int VariableIndex,
        string VariableName,
        double Coefficient,
        double Power,
        IReadOnlyList<double> Points,
        IReadOnlyList<double> Values,
        IReadOnlyList<string> WeightNames);

    private static IEnumerable<int> NonLinearIndices(LinearModel model) =>
        Enumerable.Range(0, model.Variables.Count).Where(i => model.Variables[i].IsNonLinear);

    /// <summary>
    /// The largest value a variable can take, worked out the way the notes find M: set the other
    /// variables to zero, divide each right-hand-side by this variable's coefficient, take the smallest.
    /// </summary>
    private static double UpperBound(LinearModel model, int index)
    {
        double bound = double.PositiveInfinity;

        foreach (Constraint constraint in model.Constraints)
        {
            double coefficient = constraint.Coefficients[index];

            if (!Num.IsPositive(coefficient) || constraint.Relation == Relation.GreaterOrEqual)
            {
                continue;
            }

            bound = Math.Min(bound, constraint.RightHandSide / coefficient);
        }

        return bound;
    }

    /// <summary>Replaces every non-linear variable with its breakpoint weights and returns the LP.</summary>
    private static (LinearModel Linear, List<BreakpointSet> Breakpoints, List<int> LinearIndices) BuildLinearModel(
        LinearModel model,
        IReadOnlyDictionary<int, double> bounds)
    {
        List<Variable> variables = [];
        List<BreakpointSet> breakpoints = [];
        List<int> linearIndices = [];

        // Each original variable becomes either itself or a block of weights.
        List<List<int>> columnsFor = [];

        for (int i = 0; i < model.Variables.Count; i++)
        {
            Variable variable = model.Variables[i];
            List<int> columns = [];

            if (!variable.IsNonLinear)
            {
                linearIndices.Add(variables.Count);
                columns.Add(variables.Count);
                variables.Add(new Variable(variable.Name, variable.ObjectiveCoefficient, variable.SignRestriction));
                columnsFor.Add(columns);
                continue;
            }

            linearIndices.Add(-1);
            double upper = bounds[i];
            List<double> points = [];
            List<double> values = [];
            List<string> names = [];

            for (int k = 0; k <= Segments; k++)
            {
                double point = upper * k / Segments;
                points.Add(point);
                values.Add(variable.ObjectiveCoefficient * Math.Pow(point, variable.ObjectivePower));
                names.Add($"L{i + 1}_{k}");

                columns.Add(variables.Count);
                variables.Add(new Variable(names[k], values[k], SignRestriction.Positive));
            }

            breakpoints.Add(new BreakpointSet(
                i, variable.Name, variable.ObjectiveCoefficient, variable.ObjectivePower, points, values, names));
            columnsFor.Add(columns);
        }

        // Every original constraint has its non-linear variable replaced by sum(bk * lambda_k).
        List<Constraint> constraints = [];

        foreach (Constraint constraint in model.Constraints)
        {
            double[] coefficients = new double[variables.Count];

            for (int i = 0; i < model.Variables.Count; i++)
            {
                if (!model.Variables[i].IsNonLinear)
                {
                    coefficients[columnsFor[i][0]] = constraint.Coefficients[i];
                    continue;
                }

                BreakpointSet set = breakpoints.First(s => s.VariableIndex == i);
                for (int k = 0; k < set.Points.Count; k++)
                {
                    coefficients[columnsFor[i][k]] = constraint.Coefficients[i] * set.Points[k];
                }
            }

            constraints.Add(new Constraint(constraint.Name, coefficients, constraint.Relation, constraint.RightHandSide));
        }

        // The weights of each non-linear variable have to add up to exactly 1.
        foreach (BreakpointSet set in breakpoints)
        {
            double[] coefficients = new double[variables.Count];
            foreach (int column in columnsFor[set.VariableIndex])
            {
                coefficients[column] = 1.0;
            }

            constraints.Add(new Constraint($"sumL{set.VariableIndex + 1}", coefficients, Relation.Equal, 1.0));
        }

        return (new LinearModel(model.Sense, variables, constraints), breakpoints, linearIndices);
    }
}
