using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;

namespace LPR381.Tests;

/// <summary>
/// Checks every algorithm the brief asks for against a worked example, and checks that they agree
/// with each other where they should.
/// </summary>
public class AlgorithmTests
{
    private const string Santa = CanonicalFormBuilderTests.SantaModel;

    /// <summary>A small pure IP: the relaxed answer is fractional, the integer answer is not.</summary>
    private const string IntegerModel = """
        max +5 +4
        +6 +4 <=24
        +1 +2 <=6
        int int
        """;

    /// <summary>The knapsack worked all the way through in Section 12 of the study notes.</summary>
    private const string NotesKnapsack = """
        max +4 +2 +2 +1 +10
        +12 +2 +1 +1 +4 <=15
        bin bin bin bin bin
        """;

    /// <summary>The knapsack from the brief.</summary>
    private const string BriefKnapsack = """
        max +2 +3 +3 +5 +2 +4
        +11 +8 +6 +14 +10 +10 <=40
        bin bin bin bin bin bin
        """;

    private static LinearModel Parse(string text) => ModelFileParser.ParseText(text);

    // ---------- Revised Primal Simplex ----------

    [Fact]
    public void Revised_simplex_reaches_the_same_answer_as_the_tableau_simplex()
    {
        SolveResult tableau = new LPR381.Core.Simplex.PrimalSimplexSolver().Solve(Parse(Santa));
        SolveResult revised = new RevisedPrimalSimplexSolver().Solve(Parse(Santa));

        Assert.Equal(SolutionStatus.Optimal, revised.Status);
        Assert.Equal(tableau.ObjectiveValue, revised.ObjectiveValue, 6);
        Assert.Equal(tableau.VariableValues!["x1"], revised.VariableValues!["x1"], 6);
        Assert.Equal(tableau.VariableValues["x2"], revised.VariableValues["x2"], 6);
    }

    [Fact]
    public void Revised_simplex_shows_price_out_and_product_form_at_every_iteration()
    {
        SolveResult result = new RevisedPrimalSimplexSolver().Solve(Parse(Santa));

        Assert.Contains(result.Steps, step => step.Heading.Contains("PRICE OUT"));
        Assert.Contains(result.Steps, step => step.Heading.Contains("PRODUCT FORM"));
        Assert.Contains(result.Steps, step => step.Body.Contains("B-inverse"));
        Assert.Contains(result.Steps, step => step.Body.Contains("CBV x B-inverse"));
        Assert.Contains(result.Steps, step => step.Body.Contains("eta matrix"));
    }

    [Fact]
    public void Revised_simplex_handles_a_model_that_needs_the_dual_simplex_first()
    {
        SolveResult result = new RevisedPrimalSimplexSolver().Solve(Parse("""
            min +2 +3
            +1 +1 >=10
            +1 +0 >=3
            + +
            """));

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(20, result.ObjectiveValue, 3);
    }

    // ---------- Branch & Bound Simplex ----------

    [Fact]
    public void Branch_and_bound_finds_the_integer_optimum()
    {
        SolveResult result = new BranchAndBoundSimplexSolver().Solve(Parse(IntegerModel));

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(20, result.ObjectiveValue, 3);
        Assert.Equal(4, result.VariableValues!["x1"], 3);
        Assert.Equal(0, result.VariableValues["x2"], 3);
    }

    [Fact]
    public void Branch_and_bound_shows_its_sub_problems_and_names_the_best_candidate()
    {
        SolveResult result = new BranchAndBoundSimplexSolver().Solve(Parse(IntegerModel));

        Assert.Contains(result.Steps, step => step.Heading.Contains("SP0"));
        Assert.Contains(result.Steps, step => step.Heading.Contains("CANDIDATE"));
        Assert.Contains(result.Steps, step => step.Heading == "BEST CANDIDATE");
        Assert.Contains(result.Steps, step => step.Heading == "Branch & Bound tree");

        // Both children of every branch must be created: an upper (>=) and a lower (<=).
        Assert.Contains(result.Steps, step => step.Body.Contains(">=") && step.Heading.StartsWith("Branching on"));
    }

    [Fact]
    public void Branch_and_bound_solves_the_brief_knapsack_as_an_integer_programme()
    {
        SolveResult result = new BranchAndBoundSimplexSolver().Solve(Parse(BriefKnapsack));

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(BruteForceKnapsack(BriefKnapsack), result.ObjectiveValue, 3);
    }

    [Fact]
    public void Branch_and_bound_turns_away_a_model_with_nothing_to_branch_on()
    {
        Assert.False(new BranchAndBoundSimplexSolver().CanSolve(Parse(Santa), out string reason));
        Assert.Contains("nothing to branch on", reason);
    }

    // ---------- Cutting Plane ----------

    [Fact]
    public void Cutting_plane_reaches_the_same_integer_optimum_as_branch_and_bound()
    {
        SolveResult cuts = new CuttingPlaneSolver().Solve(Parse(IntegerModel));
        SolveResult branch = new BranchAndBoundSimplexSolver().Solve(Parse(IntegerModel));

        Assert.Equal(SolutionStatus.Optimal, cuts.Status);
        Assert.Equal(branch.ObjectiveValue, cuts.ObjectiveValue, 3);
        Assert.Equal(branch.VariableValues!["x1"], cuts.VariableValues!["x1"], 3);
        Assert.Equal(branch.VariableValues["x2"], cuts.VariableValues["x2"], 3);
    }

    [Fact]
    public void Cutting_plane_shows_the_fraction_split_for_each_cut()
    {
        SolveResult result = new CuttingPlaneSolver().Solve(Parse(IntegerModel));

        Assert.Contains(result.Steps, step => step.Heading.StartsWith("Cut 1"));
        Assert.Contains(result.Steps, step => step.Body.Contains("integer") && step.Body.Contains("fraction"));
    }

    // ---------- Branch & Bound Knapsack ----------

    [Fact]
    public void Knapsack_reproduces_the_worked_example_from_the_notes()
    {
        SolveResult result = new KnapsackBranchAndBoundSolver().Solve(Parse(NotesKnapsack));

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(15, result.ObjectiveValue, 3);
        Assert.Equal(0, result.VariableValues!["x1"], 3);
        Assert.Equal(1, result.VariableValues["x2"], 3);
        Assert.Equal(1, result.VariableValues["x3"], 3);
        Assert.Equal(1, result.VariableValues["x4"], 3);
        Assert.Equal(1, result.VariableValues["x5"], 3);
    }

    [Fact]
    public void Knapsack_builds_the_same_tree_the_notes_do()
    {
        SolveResult result = new KnapsackBranchAndBoundSolver().Solve(Parse(NotesKnapsack));

        // SP0 branches on x1 = 7/12; SP2 (x1 = 1) branches on x5; SP2.2 breaks the bag.
        Assert.Contains(result.Steps, step => step.Heading.StartsWith("SP0"));
        Assert.Contains(result.Steps, step => step.Heading.StartsWith("SP1"));
        Assert.Contains(result.Steps, step => step.Heading.StartsWith("SP2 "));
        Assert.Contains(result.Steps, step => step.Heading.StartsWith("SP2.1"));
        Assert.Contains(result.Steps, step => step.Heading.StartsWith("SP2.2"));
        Assert.Contains(result.Steps, step => step.Narrative.Contains("INFEASIBLE"));
        Assert.Contains(result.Steps, step => step.Heading == "BEST CANDIDATE");
    }

    [Fact]
    public void Knapsack_ranks_by_z_over_c_not_c_over_z()
    {
        SolveResult result = new KnapsackBranchAndBoundSolver().Solve(Parse(NotesKnapsack));
        IterationStep ranking = result.Steps.First(step => step.Heading.StartsWith("Step 1 and 2"));

        // The notes' order: x5 -> x3 -> x2 -> x4 -> x1.
        Assert.Contains("x5 -> x3 -> x2 -> x4 -> x1", ranking.Body);
    }

    [Fact]
    public void Knapsack_matches_a_brute_force_search_of_the_brief_example()
    {
        SolveResult result = new KnapsackBranchAndBoundSolver().Solve(Parse(BriefKnapsack));

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(BruteForceKnapsack(BriefKnapsack), result.ObjectiveValue, 3);
    }

    [Fact]
    public void Knapsack_refuses_a_model_with_more_than_one_bag()
    {
        Assert.False(new KnapsackBranchAndBoundSolver().CanSolve(Parse("""
            max +2 +3
            +11 +8 <=40
            +1 +1 <=3
            bin bin
            """), out string reason));

        Assert.Contains("exactly one constraint", reason);
    }

    // ---------- Non-linear bonus ----------

    [Fact]
    public void Linear_solvers_turn_away_a_non_linear_model()
    {
        LinearModel model = Parse("""
            max +1^2 +3
            +1 +1 <=4
            + +
            """);

        Assert.True(model.IsNonLinear);
        Assert.False(new LPR381.Core.Simplex.PrimalSimplexSolver().CanSolve(model, out string reason));
        Assert.Contains("Golden Section Search", reason);
    }

    /// <summary>Checks the 0/1 knapsack answer by trying every combination.</summary>
    private static double BruteForceKnapsack(string modelText)
    {
        LinearModel model = Parse(modelText);
        Constraint bag = model.Constraints[0];
        int count = model.Variables.Count;
        double best = 0.0;

        for (int mask = 0; mask < 1 << count; mask++)
        {
            double weight = 0.0;
            double value = 0.0;

            for (int i = 0; i < count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    weight += bag.Coefficients[i];
                    value += model.Variables[i].ObjectiveCoefficient;
                }
            }

            if (weight <= bag.RightHandSide && value > best)
            {
                best = value;
            }
        }

        return best;
    }
}
