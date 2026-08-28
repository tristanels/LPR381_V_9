using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.NonLinear;

namespace LPR381.Tests;

/// <summary>
/// Checks the Golden Section Search against the worked example in study notes section 18, and checks
/// the function reader that lets that example be written into an input file at all.
/// </summary>
public class GoldenSectionTests
{
    // ---------- reading the function ----------

    [Theory]
    [InlineData("x^2", 3, 9)]
    [InlineData("-x^2 + 4x", 2, 4)]
    [InlineData("2x", 5, 10)]                 // implicit multiplication
    [InlineData("(1 + x)(2 - x)", 1, 2)]      // two brackets side by side
    [InlineData("4sin(x)(1 + cos(x))", 0, 0)] // the notes' function
    [InlineData("sqrt(x) + ln(x)", 1, 1)]
    [InlineData("2^3", 0, 8)]                 // right-associative power on constants
    public void Reads_and_evaluates_a_function(string text, double x, double expected)
    {
        FunctionExpression function = FunctionExpression.Parse(text);
        Assert.Equal(expected, function.Evaluate(x), 9);
    }

    [Fact]
    public void Reads_the_notes_function_written_without_brackets()
    {
        // The notes write it as "4sinx(1 + cosx)", so "sinx" has to come apart into sin and x.
        FunctionExpression bare = FunctionExpression.Parse("4sinx(1 + cosx)");
        FunctionExpression bracketed = FunctionExpression.Parse("4sin(x)(1 + cos(x))");

        Assert.Equal(bracketed.Evaluate(0.9), bare.Evaluate(0.9), 9);
    }

    [Theory]
    [InlineData("4sin(x")]
    [InlineData("2 +")]
    [InlineData("2 $ 3")]
    [InlineData("wibble(x)")]
    public void Rejects_a_function_it_cannot_read(string text) =>
        Assert.Throws<FunctionParseException>(() => FunctionExpression.Parse(text));

    // ---------- the worked example from the notes ----------

    private const string NotesExample = """
        max 4sin(x)(1 + cos(x))
        0 <= x <= 1.5707963268
        tolerance 0.05
        """;

    [Fact]
    public void Reproduces_the_first_two_iterations_from_the_notes()
    {
        // Notes section 18:
        //   iter 1: lower 0,          upper 1.57079633, d 0.97080552, X1 0.97080552, X2 0.59999081
        //   iter 2: lower 0.59999081, upper 1.57079633, d 0.59999081, X1 1.19998161, X2 0.97080552
        NonLinearModel model = NonLinearFileParser.ParseText(NotesExample);
        SolveResult result = new GoldenSectionSolver().Solve(model);

        IterationStep first = result.Steps.First(step => step.Heading == "Iteration 1");
        Assert.Contains("0.970806", first.Body);   // X1
        Assert.Contains("0.599991", first.Body);   // X2
        Assert.Contains("5.165428", first.Body);   // f(X1)
        Assert.Contains("4.122604", first.Body);   // f(X2)

        // f(X2) < f(X1), so the notes drop the lower end and X2 becomes the new x_lower.
        Assert.Contains("becomes the new x_lower", first.Narrative);

        IterationStep second = result.Steps.First(step => step.Heading == "Iteration 2");
        Assert.Contains("1.199982", second.Body);  // X1
        Assert.Contains("0.970806", second.Body);  // X2, carried over from iteration 1
        Assert.Contains("carried over", second.Body);
        Assert.Contains("becomes the new x_upper", second.Narrative);
    }

    [Fact]
    public void Finds_the_optimum_the_notes_report()
    {
        // The notes finish at x = 1.04162483 rad with f = 5.19599088.
        SolveResult result = new GoldenSectionSolver().Solve(NonLinearFileParser.ParseText(NotesExample));

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(1.0416248, result.VariableValues!["x"], 5);
        Assert.Equal(5.1959909, result.ObjectiveValue, 5);
    }

    [Fact]
    public void Stops_once_the_interval_is_inside_the_tolerance()
    {
        SolveResult result = new GoldenSectionSolver().Solve(NonLinearFileParser.ParseText(NotesExample));

        // Every iteration is kept, plus the method, the summary table and the answer.
        Assert.Contains(result.Steps, step => step.Heading == "Method - Golden Section Search (study notes section 18)");
        Assert.Contains(result.Steps, step => step.Heading == "The search table");
        Assert.Contains(result.Steps, step => step.Heading == "Answer");
        Assert.True(result.Steps.Count(step => step.Heading.StartsWith("Iteration ")) >= 6);
    }

    [Fact]
    public void Only_one_new_evaluation_of_f_is_needed_after_the_first_iteration()
    {
        SolveResult result = new GoldenSectionSolver().Solve(NonLinearFileParser.ParseText(NotesExample));

        int iterations = result.Steps.Count(step => step.Heading.StartsWith("Iteration "));
        string summary = result.Steps.First(step => step.Heading == "The search table").Narrative;

        // Two to start, one per iteration after that, plus one for the midpoint at the end.
        Assert.Contains($"{iterations + 2} evaluation(s)", summary);
    }

    // ---------- max, min and awkward models ----------

    [Fact]
    public void Finds_an_interior_maximum()
    {
        // max 4x - x^2 peaks at x = 2, f = 4.
        SolveResult result = Solve("""
            max 4x - x^2
            0 <= x <= 5
            tolerance 0.0001
            """);

        Assert.Equal(2.0, result.VariableValues!["x"], 4);
        Assert.Equal(4.0, result.ObjectiveValue, 4);
    }

    [Fact]
    public void Finds_an_interior_minimum_by_flipping_the_comparison()
    {
        // min x^2 - 6x + 13 troughs at x = 3, f = 4.
        SolveResult result = Solve("""
            min x^2 - 6x + 13
            0 <= x <= 10
            tolerance 0.0001
            """);

        Assert.Equal(3.0, result.VariableValues!["x"], 4);
        Assert.Equal(4.0, result.ObjectiveValue, 4);
    }

    [Fact]
    public void A_monotonic_function_converges_on_the_end_of_the_interval()
    {
        // x^2 only climbs on [0, 4], so the optimum sits at the upper bound.
        SolveResult result = Solve("""
            max x^2
            0 <= x <= 4
            tolerance 0.0001
            """);

        Assert.Equal(4.0, result.VariableValues!["x"], 3);
        Assert.Equal(16.0, result.ObjectiveValue, 2);
    }

    [Fact]
    public void Reports_a_function_that_is_undefined_inside_the_interval()
    {
        // ln is undefined for negative x, and this interval puts an interior point at -0.236.
        SolveResult result = Solve("""
            max ln(x)
            -1 <= x <= 1
            tolerance 0.01
            """);

        Assert.Equal(SolutionStatus.Error, result.Status);
        Assert.Contains("not defined", result.Message);
    }

    [Fact]
    public void An_endpoint_the_function_cannot_reach_is_not_a_problem()
    {
        // The search only ever evaluates points strictly inside the interval, so ln(0) is never
        // asked for even though the interval starts at 0. The optimum is the upper bound, x = 1.
        SolveResult result = Solve("""
            max ln(x)
            0 <= x <= 1
            tolerance 0.001
            """);

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(1.0, result.VariableValues!["x"], 3);
    }

    // ---------- the input file ----------

    [Fact]
    public void Tells_the_two_input_formats_apart()
    {
        Assert.True(NonLinearFileParser.LooksNonLinear(Lines("max 4sin(x)(1 + cos(x))", "0 <= x <= 1.6")));
        Assert.True(NonLinearFileParser.LooksNonLinear(Lines("# a comment first", "min x^2 - 6x + 13", "0 <= x <= 10")));

        // A linear model, even one carrying a ^ power on a coefficient, is not this format.
        Assert.False(NonLinearFileParser.LooksNonLinear(Lines("max +2 +3", "+1 +1 <=4", "+ +")));
        Assert.False(NonLinearFileParser.LooksNonLinear(Lines("max +1^2 +3", "+1 +1 <=4", "+ +")));
    }

    [Fact]
    public void Reads_the_sense_interval_and_tolerance()
    {
        NonLinearModel model = NonLinearFileParser.ParseText("""
            # the tolerance line may be left off
            min f(x) = x^2
            -2 <= x <= 6
            """);

        Assert.Equal(ObjectiveSense.Min, model.Sense);
        Assert.Equal(-2, model.Lower, 9);
        Assert.Equal(6, model.Upper, 9);
        Assert.Equal(NonLinearFileParser.DefaultTolerance, model.Tolerance, 9);
    }

    [Theory]
    [InlineData("max 4sin(x)", "A non-linear model needs a second line")]
    [InlineData("max 4sin(x)\nnot an interval at all here", "is not an interval")]
    [InlineData("max 4sin(x)\n5 <= x <= 1", "interval is empty")]
    [InlineData("max 4sin(x)\n0 <= x <= 1\ntolerance -3", "must be above zero")]
    public void Reports_a_bad_non_linear_file_with_the_line_number(string text, string expected)
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() => NonLinearFileParser.ParseText(text));
        Assert.Contains(expected, error.Message);
    }

    private static SolveResult Solve(string text) =>
        new GoldenSectionSolver().Solve(NonLinearFileParser.ParseText(text));

    private static string[] Lines(params string[] lines) => lines;
}
