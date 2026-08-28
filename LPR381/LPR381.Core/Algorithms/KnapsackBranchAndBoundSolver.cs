using System.Text;
using LPR381.Core.IO;
using LPR381.Core.Models;

namespace LPR381.Core.Algorithms;

/// <summary>
/// The Branch &amp; Bound Knapsack Algorithm, Section 12 of the study notes:
/// <c>ONE CONSTRAINT -> Z/C -> RANK -> FILL -> LEFT/NEEDED -> BRANCH 0/1 -> FORCE -> CANDIDATE -> BEST Z</c>.
/// <list type="number">
/// <item>Ratio test: z coefficient / constraint coefficient (Z/C, not C/Z).</item>
/// <item>Rank: largest ratio first for a max problem, smallest first for a min.</item>
/// <item>Fill the bag in rank order, subtracting each item's coefficient from the capacity.</item>
/// <item>The first item that does not fit gets the fraction capacity LEFT / capacity NEEDED.</item>
/// <item>That fraction is the branching variable: split into x = 0 and x = 1.</item>
/// <item>A forced value is absolute in that branch and is inherited by every node below it.</item>
/// <item>Classify: fraction present -> branch and no z; all binary and feasible -> candidate, work out
/// z; constraint broken -> infeasible, prune.</item>
/// </list>
/// This is not a tableau method, so it does not use the simplex - it is the ranking algorithm taught
/// in the module, and every node's working is displayed.
/// </summary>
public sealed class KnapsackBranchAndBoundSolver : ISolver
{
    /// <summary>Stops a runaway tree; the module's problems are far smaller than this.</summary>
    public const int MaxNodes = 500;

    public string Name => "Branch & Bound Knapsack Algorithm";

    public string Description =>
        "For binary models with a single capacity constraint: rank by z/c, fill the bag, branch on the fractional item.";

    public bool IsImplemented => true;

    public bool CanSolve(LinearModel model, out string reason)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.IsNonLinear)
        {
            reason = "This model has a non-linear objective term (a ^ power). Write it as a function of one variable and solve it with the Golden Section Search - see sample golden.txt.";
            return false;
        }

        // The entry condition from the notes: ONE BAG = ONE CONSTRAINT, and every variable binary.
        if (!model.IsBinaryModel)
        {
            reason = "Knapsack Branch & Bound needs every decision variable to be binary (bin).";
            return false;
        }

        if (model.Constraints.Count != 1)
        {
            reason =
                $"Knapsack Branch & Bound works with exactly one constraint (one bag) but this model has " +
                $"{model.Constraints.Count}. Use Branch & Bound Simplex instead.";
            return false;
        }

        if (model.Constraints[0].Relation != Relation.LessOrEqual)
        {
            reason = "The knapsack constraint must be a <= capacity constraint.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>How a node came out.</summary>
    private enum NodeKind
    {
        Fractional,
        Candidate,
        Infeasible
    }

    /// <summary>The result of filling the bag at one node.</summary>
    private sealed record Fill(
        NodeKind Kind,
        double[] Values,
        int FractionalIndex,
        double Objective,
        string Working);

    /// <summary>A sub-problem: the values that have been forced by the branches above it.</summary>
    private sealed record Node(string Id, IReadOnlyDictionary<int, double> Forced, string Branch);

    public SolveResult Solve(LinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!CanSolve(model, out string why))
        {
            return SolveResult.Failed(Name, SolutionStatus.Error, why);
        }

        Constraint bag = model.Constraints[0];
        int count = model.Variables.Count;

        // Step 1 and 2: ratio test and ranking.
        int[] order = RankOrder(model, bag, out double[] ratios);
        List<IterationStep> steps = [];
        steps.Add(IterationStep.Text("Step 1 and 2 - ratio test and ranking", DescribeRanking(model, bag, ratios, order),
            model.Sense == ObjectiveSense.Max
                ? "Max problem: largest ratio ranks first. A tie goes to the smaller subscript."
                : "Min problem: smallest ratio ranks first. A tie goes to the smaller subscript."));

        List<IReadOnlyList<string>> summary = [];
        var pending = new Stack<Node>();
        pending.Push(new Node("SP0", new Dictionary<int, double>(), "(nothing forced)"));

        double[]? bestValues = null;
        double bestObjective = model.Sense == ObjectiveSense.Max ? double.NegativeInfinity : double.PositiveInfinity;
        string bestNode = string.Empty;
        int nodes = 0;

        while (pending.Count > 0 && nodes < MaxNodes)
        {
            Node node = pending.Pop();
            nodes++;

            Fill fill = FillBag(model, bag, order, ratios, node.Forced);

            switch (fill.Kind)
            {
                case NodeKind.Infeasible:
                    steps.Add(IterationStep.Text(
                        $"{node.Id} - {node.Branch}",
                        fill.Working,
                        "The forced items already break the capacity constraint. INFEASIBLE - no z, prune this branch."));
                    summary.Add([node.Id, node.Branch, "-", "INFEASIBLE - pruned"]);
                    break;

                case NodeKind.Candidate:
                {
                    bool isBest = bestValues is null || IsBetter(fill.Objective, bestObjective, model.Sense);
                    if (isBest)
                    {
                        bestValues = fill.Values;
                        bestObjective = fill.Objective;
                        bestNode = node.Id;
                    }

                    steps.Add(IterationStep.Text(
                        $"{node.Id} - {node.Branch}",
                        fill.Working,
                        $"Every variable is 0 or 1 and the bag holds, so this node is a CANDIDATE with " +
                        $"z = {Num.Format(fill.Objective)}." +
                        (isBest ? " Best candidate so far." : " The incumbent is still better.")));

                    summary.Add([node.Id, node.Branch, Num.Format(fill.Objective),
                        isBest ? "CANDIDATE (best so far)" : "CANDIDATE"]);
                    break;
                }

                default:
                {
                    Variable branchVariable = model.Variables[fill.FractionalIndex];
                    double fraction = fill.Values[fill.FractionalIndex];

                    steps.Add(IterationStep.Text(
                        $"{node.Id} - {node.Branch}",
                        fill.Working,
                        $"{branchVariable.Name} came out as {Num.Format(fraction)}, which is not binary, so this node " +
                        "is NOT a candidate and has no z. Branch on it: one sub-problem with " +
                        $"{branchVariable.Name} = 0 and one with {branchVariable.Name} = 1. Every value forced above " +
                        "stays forced all the way down each branch."));

                    summary.Add([node.Id, node.Branch, "-", $"fraction {branchVariable.Name} = {Num.Format(fraction)}, branch"]);

                    string prefix = node.Id == "SP0" ? "SP" : node.Id + ".";
                    string zeroId = node.Id == "SP0" ? "SP1" : prefix + "1";
                    string oneId = node.Id == "SP0" ? "SP2" : prefix + "2";

                    // Pushed in reverse so the x = 0 branch is explored first, as the notes lay it out.
                    pending.Push(new Node(oneId, Force(node.Forced, fill.FractionalIndex, 1.0),
                        Describe(node.Branch, branchVariable.Name, 1.0)));
                    pending.Push(new Node(zeroId, Force(node.Forced, fill.FractionalIndex, 0.0),
                        Describe(node.Branch, branchVariable.Name, 0.0)));
                    break;
                }
            }
        }

        string overrun = nodes >= MaxNodes
            ? $" The search stopped at the {MaxNodes} node limit, so the answer may not be proven optimal."
            : string.Empty;

        steps.Add(IterationStep.Text(
            "Branch & Bound Knapsack tree",
            MatrixFormatter.Table(["node", "forced", "z", "outcome"], summary),
            $"{nodes} node(s) explored. Every sub-problem was created and fathomed - a fractional or infeasible " +
            "node never gets a z value." + overrun));

        if (bestValues is null)
        {
            const string none = "No candidate was found - every branch broke the capacity constraint.";
            return SolveResult.Failed(Name, SolutionStatus.Infeasible, none, null, steps);
        }

        Dictionary<string, double> solution = [];
        for (int i = 0; i < count; i++)
        {
            solution[model.Variables[i].Name] = bestValues[i];
        }

        steps.Add(IterationStep.Text(
            "BEST CANDIDATE",
            $"  Node : {bestNode}\n" +
            $"  z    : {Num.Format(bestObjective)}\n" +
            string.Concat(model.Variables.Select(v => $"  {v.Name}    : {Num.Format(solution[v.Name])}\n")),
            model.Sense == ObjectiveSense.Max
                ? "Max problem, so the candidate with the highest z wins."
                : "Min problem, so the candidate with the lowest z wins."));

        return SolveResult.Optimal(
            Name,
            CanonicalFormOrNull(model),
            steps,
            bestObjective,
            solution,
            $"Best candidate found at node {bestNode}." + overrun);
    }

    /// <summary>
    /// Fills the bag for one node: forced values first, then everything else in rank order.
    /// </summary>
    private static Fill FillBag(
        LinearModel model,
        Constraint bag,
        int[] order,
        double[] ratios,
        IReadOnlyDictionary<int, double> forced)
    {
        int count = model.Variables.Count;
        double[] values = new double[count];
        double remaining = bag.RightHandSide;
        List<IReadOnlyList<string>> rows = [];

        // Whatever a branch forced is absolute here, even if the ranking would not have chosen it.
        foreach ((int index, double value) in forced.OrderBy(pair => pair.Key))
        {
            values[index] = value;
            if (Num.IsZero(value))
            {
                rows.Add([model.Variables[index].Name, "forced", "0", Num.Format(remaining)]);
                continue;
            }

            remaining -= bag.Coefficients[index];
            rows.Add([model.Variables[index].Name, "forced", "1", Num.Format(remaining)]);

            if (Num.IsNegative(remaining))
            {
                var broken = new StringBuilder();
                broken.AppendLine("  Filling the bag, forced values first:");
                broken.Append(MatrixFormatter.Table(["variable", "why", "take", "capacity left"], rows));
                broken.AppendLine();
                broken.AppendLine($"  Capacity left is {Num.Format(remaining)}, which is below zero.");
                return new Fill(NodeKind.Infeasible, values, -1, 0.0, broken.ToString());
            }
        }

        int fractionalIndex = -1;

        foreach (int index in order)
        {
            if (forced.ContainsKey(index))
            {
                continue;
            }

            if (fractionalIndex >= 0)
            {
                // Everything after the fraction stays out of the bag.
                rows.Add([model.Variables[index].Name, $"rank {Array.IndexOf(order, index) + 1}", "0", Num.Format(remaining)]);
                continue;
            }

            double needed = bag.Coefficients[index];

            if (needed <= remaining + Num.Epsilon)
            {
                values[index] = 1.0;
                remaining -= needed;
                rows.Add([model.Variables[index].Name, $"rank {Array.IndexOf(order, index) + 1}", "1", Num.Format(remaining)]);
            }
            else if (Num.IsZero(remaining) || !Num.IsPositive(needed))
            {
                values[index] = 0.0;
                rows.Add([model.Variables[index].Name, $"rank {Array.IndexOf(order, index) + 1}", "0", Num.Format(remaining)]);
            }
            else
            {
                // Step 4: the item does not fit, so it takes the fraction LEFT / NEEDED.
                double fraction = remaining / needed;
                values[index] = fraction;
                fractionalIndex = index;
                rows.Add(
                [
                    model.Variables[index].Name,
                    $"rank {Array.IndexOf(order, index) + 1}",
                    $"{Num.Format(remaining)}/{Num.Format(needed)} = {Num.Format(fraction)}",
                    Num.Format(0.0)
                ]);

                // The fraction uses up whatever was left, so nothing after it can fit either.
                remaining = 0.0;
            }
        }

        var working = new StringBuilder();
        working.AppendLine($"  Capacity: {Num.Format(bag.RightHandSide)}");
        working.AppendLine("  Filling the bag, forced values first, then the rest in rank order:");
        working.Append(MatrixFormatter.Table(["variable", "why", "take", "capacity left"], rows));
        working.AppendLine();

        if (fractionalIndex >= 0)
        {
            working.AppendLine(
                $"  {model.Variables[fractionalIndex].Name} is fractional, so this node is not a candidate and has no z.");
            return new Fill(NodeKind.Fractional, values, fractionalIndex, 0.0, working.ToString());
        }

        double objective = 0.0;
        var terms = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            objective += model.Variables[i].ObjectiveCoefficient * values[i];
            terms.Append(i == 0 ? string.Empty : " + ");
            terms.Append($"{Num.Format(model.Variables[i].ObjectiveCoefficient)}({Num.Format(values[i])})");
        }

        working.AppendLine($"  z = {terms} = {Num.Format(objective)}");
        return new Fill(NodeKind.Candidate, values, -1, objective, working.ToString());
    }

    /// <summary>Step 1 and 2: work out z/c for every variable and rank them.</summary>
    private static int[] RankOrder(LinearModel model, Constraint bag, out double[] ratios)
    {
        int count = model.Variables.Count;
        ratios = new double[count];

        for (int i = 0; i < count; i++)
        {
            double coefficient = bag.Coefficients[i];
            ratios[i] = Num.IsZero(coefficient)
                ? double.PositiveInfinity
                : model.Variables[i].ObjectiveCoefficient / coefficient;
        }

        double[] captured = ratios;
        IEnumerable<int> indices = Enumerable.Range(0, count);

        // Max ranks the largest ratio first, min the smallest. A tie goes to the smaller subscript.
        IOrderedEnumerable<int> ordered = model.Sense == ObjectiveSense.Max
            ? indices.OrderByDescending(i => captured[i]).ThenBy(i => i)
            : indices.OrderBy(i => captured[i]).ThenBy(i => i);

        return ordered.ToArray();
    }

    private static string DescribeRanking(LinearModel model, Constraint bag, double[] ratios, int[] order)
    {
        List<IReadOnlyList<string>> rows = [];

        for (int rank = 0; rank < order.Length; rank++)
        {
            int index = order[rank];
            rows.Add(
            [
                model.Variables[index].Name,
                Num.Format(model.Variables[index].ObjectiveCoefficient),
                Num.Format(bag.Coefficients[index]),
                double.IsInfinity(ratios[index]) ? "free" : Num.Format(ratios[index]),
                (rank + 1).ToString()
            ]);
        }

        var text = new StringBuilder();
        text.AppendLine("  Ratio = z coefficient / constraint coefficient (Z/C, not C/Z).");
        text.AppendLine();
        text.Append(MatrixFormatter.Table(["variable", "z", "c", "ratio", "rank"], rows));
        text.AppendLine();
        text.AppendLine("  Order: " + string.Join(" -> ", order.Select(i => model.Variables[i].Name)));
        return text.ToString();
    }

    private static Dictionary<int, double> Force(IReadOnlyDictionary<int, double> forced, int index, double value)
    {
        var next = new Dictionary<int, double>(forced) { [index] = value };
        return next;
    }

    private static string Describe(string parentBranch, string variableName, double value)
    {
        string bound = $"{variableName} = {Num.Format(value)}";
        return parentBranch.StartsWith('(') ? bound : parentBranch + ", " + bound;
    }

    private static bool IsBetter(double candidate, double incumbent, ObjectiveSense sense) =>
        sense == ObjectiveSense.Max
            ? candidate > incumbent + Num.Epsilon
            : candidate < incumbent - Num.Epsilon;

    /// <summary>
    /// The knapsack algorithm never builds a table, but the report still shows the canonical form of
    /// the model for completeness, so it is built here and handed to the result.
    /// </summary>
    private static Simplex.CanonicalForm CanonicalFormOrNull(LinearModel model) =>
        Simplex.CanonicalFormBuilder.Build(model);
}
