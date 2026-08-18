using System.Text;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Algorithms;

/// <summary>
/// The Branch &amp; Bound Simplex Algorithm, Section 4 of the study notes.
/// <list type="number">
/// <item>Solve the relaxed LP with the simplex.</item>
/// <item>Branch on the variable whose fraction is closest to 0.5 (smallest subscript breaks a tie).</item>
/// <item>Create both sub-problems: x &gt;= ceiling (upper) and x &lt;= floor (lower).</item>
/// <item>Add the bound to the parent's optimal table and fix the row so the basis is canonical again.</item>
/// <item>Re-solve (Dual Simplex first if the RHS went negative), then classify the node.</item>
/// </list>
/// The search is depth first with a stack, which is what gives the backtracking the brief asks for:
/// a node is explored to the bottom, then the search steps back to the last unexplored sibling.
/// <para>
/// Every table of every sub-problem is displayed, every node is fathomed with a stated reason
/// (integer candidate, infeasible, or bounded out by the incumbent), and the best candidate is
/// reported at the end.
/// </para>
/// </summary>
public sealed class BranchAndBoundSimplexSolver : ISolver
{
    /// <summary>Stops a runaway search on a badly posed model.</summary>
    public const int MaxNodes = 200;

    public string Name => "Branch & Bound Simplex Algorithm";

    public string Description =>
        "Solves Integer Programming models by branching on fractional variables, with backtracking and fathoming.";

    public bool IsImplemented => true;

    public bool CanSolve(LinearModel model, out string reason)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.IsNonLinear)
        {
            reason = "This model has a non-linear objective term (a ^ power). Use the Non-Linear solver.";
            return false;
        }

        if (!model.IsIntegerModel)
        {
            reason =
                "No variable is int or bin, so there is nothing to branch on. " +
                "The Primal Simplex already gives the exact answer for this model.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>One sub-problem waiting to be explored.</summary>
    private sealed record Node(string Id, Tableau Start, string Bound, IReadOnlyList<string> Path);

    public SolveResult Solve(LinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        CanonicalForm form = CanonicalFormBuilder.Build(model);
        List<IterationStep> steps = [];
        List<IReadOnlyList<string>> summary = [];

        // The root node is the relaxed LP.
        steps.Add(IterationStep.Text(
            "SP0 - the relaxed LP",
            "  The integer restrictions are dropped and the model is solved as a normal LP.\n" +
            "  Whatever comes out fractional is what gets branched on.",
            string.Empty));

        SimplexRun root = SimplexEngine.Solve(form.InitialTableau, model.Sense, steps, "SP0 T");

        if (root.Outcome != PivotOutcome.Optimal)
        {
            steps.Add(new IterationStep(root.Tableau, $"{root.Tableau.Label} (final)", root.Message));
            SolutionStatus status = root.Outcome switch
            {
                PivotOutcome.Unbounded => SolutionStatus.Unbounded,
                PivotOutcome.Infeasible => SolutionStatus.Infeasible,
                _ => SolutionStatus.Error
            };
            return SolveResult.Failed(Name, status, "The relaxed LP could not be solved: " + root.Message, form, steps);
        }

        steps.Add(new IterationStep(
            root.Tableau,
            $"{root.Tableau.Label} (SP0 relaxed optimum)",
            root.Message + " " + DescribeSolution(model, form, root.Tableau)));

        summary.Add(["SP0", "(relaxed LP)", Num.Format(root.Tableau.ObjectiveValue), ClassifyText(model, form, root.Tableau)]);

        Tableau? bestTableau = null;
        double bestObjective = model.Sense == ObjectiveSense.Max ? double.NegativeInfinity : double.PositiveInfinity;
        string bestNode = string.Empty;

        // The root is already a candidate when nothing came out fractional.
        FractionalVariable? rootBranch = IntegerRules.SelectBranchingVariable(model, form, root.Tableau);
        if (rootBranch is null)
        {
            bestTableau = root.Tableau;
            bestObjective = root.Tableau.ObjectiveValue;
            bestNode = "SP0";
        }

        // Depth first, so the search always backtracks to the most recent unexplored sibling.
        var pending = new Stack<Node>();
        if (rootBranch is not null)
        {
            PushChildren(pending, form, root.Tableau, rootBranch, "SP0", [], steps);
        }

        int nodesExplored = 0;

        while (pending.Count > 0 && nodesExplored < MaxNodes)
        {
            Node node = pending.Pop();
            nodesExplored++;

            steps.Add(IterationStep.Text(
                $"{node.Id} - branch on {node.Bound}",
                $"  Forced so far: {(node.Path.Count == 0 ? "(nothing)" : string.Join(", ", node.Path))}\n" +
                $"  New bound    : {node.Bound}\n" +
                "  The bound is added to the parent's optimal table and the row is fixed so the basis is\n" +
                "  canonical again. A negative RHS sends the table to the Dual Simplex first.",
                string.Empty));

            SimplexRun run = SimplexEngine.Solve(node.Start, model.Sense, steps, $"{node.Id} T");

            if (run.Outcome == PivotOutcome.Infeasible)
            {
                steps.Add(new IterationStep(run.Tableau, $"{run.Tableau.Label} ({node.Id} INFEASIBLE)", run.Message));
                summary.Add([node.Id, node.Bound, "-", "INFEASIBLE - fathomed"]);
                continue;
            }

            if (run.Outcome != PivotOutcome.Optimal)
            {
                steps.Add(new IterationStep(run.Tableau, $"{run.Tableau.Label} ({node.Id} stopped)", run.Message));
                summary.Add([node.Id, node.Bound, "-", "STOPPED - " + run.Outcome]);
                continue;
            }

            double objective = run.Tableau.ObjectiveValue;

            // Bounding: a node whose relaxed z cannot beat the incumbent can never contain a better
            // integer answer, so the whole branch is fathomed here.
            if (bestTableau is not null && !IsBetter(objective, bestObjective, model.Sense))
            {
                steps.Add(new IterationStep(
                    run.Tableau,
                    $"{run.Tableau.Label} ({node.Id} FATHOMED)",
                    $"z = {Num.Format(objective)} is no better than the best candidate so far " +
                    $"({Num.Format(bestObjective)} at {bestNode}), so this branch is fathomed."));
                summary.Add([node.Id, node.Bound, Num.Format(objective), "FATHOMED - cannot beat the incumbent"]);
                continue;
            }

            FractionalVariable? branch = IntegerRules.SelectBranchingVariable(model, form, run.Tableau);

            if (branch is null)
            {
                bool isBest = bestTableau is null || IsBetter(objective, bestObjective, model.Sense);
                if (isBest)
                {
                    bestTableau = run.Tableau;
                    bestObjective = objective;
                    bestNode = node.Id;
                }

                steps.Add(new IterationStep(
                    run.Tableau,
                    $"{run.Tableau.Label} ({node.Id} CANDIDATE)",
                    $"Every int/bin variable came out whole, so this node is a CANDIDATE with z = {Num.Format(objective)}. " +
                    DescribeSolution(model, form, run.Tableau) +
                    (isBest ? " This is the best candidate so far." : " The incumbent is still better.")));

                summary.Add([node.Id, node.Bound, Num.Format(objective), isBest ? "CANDIDATE (best so far)" : "CANDIDATE"]);
                continue;
            }

            steps.Add(new IterationStep(
                run.Tableau,
                $"{run.Tableau.Label} ({node.Id} optimum - still fractional)",
                run.Message + " " + DescribeSolution(model, form, run.Tableau)));

            summary.Add([node.Id, node.Bound, Num.Format(objective), $"branch on {branch.Variable.Name} = {Num.Format(branch.Value)}"]);

            PushChildren(pending, form, run.Tableau, branch, node.Id, node.Path, steps);
        }

        string overrun = nodesExplored >= MaxNodes
            ? $" The search stopped at the {MaxNodes} node limit, so the answer may not be proven optimal."
            : string.Empty;

        steps.Add(IterationStep.Text(
            "Branch & Bound tree",
            MatrixFormatter.Table(["node", "bound", "z", "outcome"], summary),
            $"{nodesExplored} sub-problem(s) explored." + overrun));

        if (bestTableau is null)
        {
            const string none = "No integer candidate was found - every branch was fathomed as infeasible.";
            return SolveResult.Failed(Name, SolutionStatus.Infeasible, none, form, steps);
        }

        IReadOnlyDictionary<string, double> solution = form.ReadSolution(bestTableau);

        steps.Add(IterationStep.Text(
            "BEST CANDIDATE",
            $"  Node : {bestNode}\n" +
            $"  z    : {Num.Format(bestObjective)}\n" +
            string.Concat(model.Variables.Select(v => $"  {v.Name}    : {Num.Format(solution[v.Name])}\n")),
            model.Sense == ObjectiveSense.Max
                ? "The candidate with the highest z wins for a max problem."
                : "The candidate with the lowest z wins for a min problem."));

        return SolveResult.Optimal(
            Name,
            form,
            steps,
            bestObjective,
            solution,
            $"Best integer candidate found at node {bestNode}." + overrun,
            bestTableau);
    }

    /// <summary>
    /// Creates both sub-problems for a fractional variable and pushes them. The notes take the upper
    /// bound first (Sub-Problem A), so the lower one is pushed first and the upper is popped first.
    /// </summary>
    private static void PushChildren(
        Stack<Node> pending,
        CanonicalForm form,
        Tableau parent,
        FractionalVariable branch,
        string parentId,
        IReadOnlyList<string> parentPath,
        List<IterationStep> steps)
    {
        int column = form.ColumnOf(branch.Variable.Name);
        double lower = Math.Floor(branch.Value);
        double upper = Math.Ceiling(branch.Value);

        // The root's children are SP1 and SP2; deeper nodes extend their parent's number.
        string prefix = parentId == "SP0" ? "SP" : parentId + ".";
        string upperId = prefix + "1";
        string lowerId = prefix + "2";

        steps.Add(IterationStep.Text(
            $"Branching on {branch.Variable.Name}",
            $"  {branch.Variable.Name} = {Num.Format(branch.Value)}, fraction {Num.Format(branch.Fraction)} " +
            $"(distance from 0.5 = {Num.Format(branch.DistanceFromHalf)}).\n" +
            $"  Sub-problem {upperId} : {branch.Variable.Name} >= {Num.Format(upper)}   (upper)\n" +
            $"  Sub-problem {lowerId} : {branch.Variable.Name} <= {Num.Format(lower)}   (lower)",
            "The branching variable is the one whose fraction is closest to 0.5; a tie goes to the smallest subscript."));

        double[] coefficients = new double[parent.RhsColumn];
        coefficients[column] = 1.0;

        string upperBound = $"{branch.Variable.Name} >= {Num.Format(upper)}";
        string lowerBound = $"{branch.Variable.Name} <= {Num.Format(lower)}";

        Tableau upperStart = TableauOperations.AddConstraint(
            parent,
            coefficients,
            Relation.GreaterOrEqual,
            upper,
            upperId,
            IntegerRules.NextLogicalName(parent, form.DecisionColumnCount, isSlack: false),
            $"{upperId} T-0");

        Tableau lowerStart = TableauOperations.AddConstraint(
            parent,
            coefficients,
            Relation.LessOrEqual,
            lower,
            lowerId,
            IntegerRules.NextLogicalName(parent, form.DecisionColumnCount, isSlack: true),
            $"{lowerId} T-0");

        // Pushed lower first so the upper branch is explored first, matching the notes' A/B order.
        pending.Push(new Node(lowerId, lowerStart, lowerBound, [.. parentPath, lowerBound]));
        pending.Push(new Node(upperId, upperStart, upperBound, [.. parentPath, upperBound]));
    }

    private static bool IsBetter(double candidate, double incumbent, ObjectiveSense sense) =>
        sense == ObjectiveSense.Max
            ? candidate > incumbent + Num.Epsilon
            : candidate < incumbent - Num.Epsilon;

    private static string DescribeSolution(LinearModel model, CanonicalForm form, Tableau tableau)
    {
        IReadOnlyDictionary<string, double> solution = form.ReadSolution(tableau);
        var text = new StringBuilder("Solution: ");
        text.Append(string.Join(", ", model.Variables.Select(v => $"{v.Name} = {Num.Format(solution[v.Name])}")));
        text.Append($", z = {Num.Format(tableau.ObjectiveValue)}.");
        return text.ToString();
    }

    private static string ClassifyText(LinearModel model, CanonicalForm form, Tableau tableau)
    {
        FractionalVariable? branch = IntegerRules.SelectBranchingVariable(model, form, tableau);
        return branch is null
            ? "CANDIDATE (already integer)"
            : $"branch on {branch.Variable.Name} = {Num.Format(branch.Value)}";
    }
}
