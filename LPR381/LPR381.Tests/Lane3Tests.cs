using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Sensitivity;
using LPR381.Core.Simplex;

namespace LPR381.Tests;

/// <summary>
/// Lane 3 regression tests: sensitivity analysis, duality, input errors and output formatting.
/// The Santa tests are checked against the SR sheet in Santas Workshop Revised.xlsx.
/// </summary>
public class Lane3Tests
{
    private const string Santa = """
        max +3 +2
        +2 +1 <=100
        +1 +1 <=80
        +1 +0 <=40
        + +
        """;

    private sealed class ScriptedInput(IEnumerable<int> choices, IEnumerable<double> numbers) : ISensitivityInput
    {
        private readonly Queue<int> _choices = new(choices);
        private readonly Queue<double> _numbers = new(numbers);

        public bool TryReadNumber(string prompt, out double value)
        {
            if (_numbers.Count == 0)
            {
                value = 0.0;
                return false;
            }

            value = _numbers.Dequeue();
            return true;
        }

        public int ReadChoice(string prompt, IReadOnlyList<string> options) =>
            _choices.Count == 0 ? -1 : _choices.Dequeue();
    }

    private static SolveResult SolveSanta() =>
        new PrimalSimplexSolver().Solve(ModelFileParser.ParseText(Santa));

    private static string Run(ISensitivityOperation operation, int[]? choices = null, double[]? numbers = null)
    {
        choices ??= [];
        numbers ??= [];
        return operation.Execute(
            SolveSanta(),
            new ScriptedInput(choices, numbers));
    }

    [Fact]
    public void Santa_shadow_prices_match_the_SR_sheet()
    {
        SensitivityContext context = SensitivityContext.From(SolveSanta())!;

        Assert.Equal([1.0, 1.0, 0.0],
            context.ConstraintShadowPrices.Select(price => Math.Round(price, 6)));
    }

    [Fact]
    public void Santa_objective_coefficient_ranges_match_the_SR_sheet()
    {
        string x1 = Run(new BasicVariableRangeOperation(), [2]);
        string x2 = Run(new BasicVariableRangeOperation(), [0]);

        Assert.Contains("Range for c             = [2.000, 4.000]", x1);
        Assert.Contains("Allowable increase      = 1.000", x1);
        Assert.Contains("Allowable decrease      = 1.000", x1);

        Assert.Contains("Range for c             = [1.500, 3.000]", x2);
        Assert.Contains("Allowable increase      = 1.000", x2);
        Assert.Contains("Allowable decrease      = 0.500", x2);
    }

    [Fact]
    public void Santa_rhs_ranges_match_the_SR_sheet()
    {
        string c1 = Run(new RhsRangeOperation(), [0]);
        string c2 = Run(new RhsRangeOperation(), [1]);
        string c3 = Run(new RhsRangeOperation(), [2]);

        Assert.Contains("Range for the RHS       = [80.000, 120.000]", c1);
        Assert.Contains("Range for the RHS       = [60.000, 100.000]", c2);
        Assert.Contains("Range for the RHS       = [20.000, +infinity]", c3);
    }

    [Fact]
    public void Santa_duality_is_strong_and_the_objective_values_match()
    {
        string output = Run(new DualityOperation());

        Assert.Contains("Primal z     = 180.000", output);
        Assert.Contains("Dual   w     = 180.000", output);
        Assert.Contains("Duality gap  = 0.000", output);
        Assert.Contains("STRONG DUALITY", output);
    }

    [Fact]
    public void Duality_respects_variable_sign_restrictions()
    {
        LinearModel max = ModelFileParser.ParseText("""
            max +3 -2 +1
            +1 +1 +1 <=10
            +1 +2 +0 >=4
            +1 +0 +1 =5
            + - urs
            """);

        (LinearModel dual, _) = DualityOperation.BuildDual(max);

        Assert.Equal(ObjectiveSense.Min, dual.Sense);
        Assert.Equal(
            [SignRestriction.Positive, SignRestriction.Negative, SignRestriction.Unrestricted],
            dual.Variables.Select(variable => variable.SignRestriction));

        Assert.Equal(Relation.GreaterOrEqual, dual.Constraints[0].Relation);
        Assert.Equal(Relation.LessOrEqual, dual.Constraints[1].Relation);
        Assert.Equal(Relation.Equal, dual.Constraints[2].Relation);
    }

    [Fact]
    public void Binary_duality_adds_the_relaxation_bound()
    {
        LinearModel binary = ModelFileParser.ParseText("""
            max +2 +3
            +1 +1 <=4
            bin bin
            """);

        (LinearModel dual, _) = DualityOperation.BuildDual(binary);

        // One original row + two x <= 1 relaxation rows become three dual variables.
        Assert.Equal(3, dual.Variables.Count);
        Assert.Equal(dual.Variables.Count, dual.Constraints[0].Coefficients.Count);
        Assert.Equal(Relation.GreaterOrEqual, dual.Constraints[0].Relation);
    }

    [Fact]
    public void Broken_input_reports_the_real_line_number()
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() =>
            ModelFileParser.ParseText("""
                # header
                max +3 +2

                +2 +1 <=100
                +1 nope <=80
                + +
                """));

        Assert.Equal(5, error.LineNumber);
        Assert.Contains("Line 5", error.Message);
        Assert.Contains("not a valid number", error.Message);
    }

    [Fact]
    public void Missing_rhs_is_reported_on_the_constraint_line()
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() =>
            ModelFileParser.ParseText("""
                max +3 +2
                +2 +1 <=
                + +
                """));

        Assert.Equal(2, error.LineNumber);
        Assert.Contains("right-hand-side", error.Message);
    }

    [Fact]
    public void Output_rounds_values_to_three_decimal_places()
    {
        SolveResult result = SolveSanta();
        var writer = new StringWriter();

        ReportWriter.Write(writer, ModelFileParser.ParseText(Santa), result);

        string output = writer.ToString();

        Assert.Contains("z = 180.000", output);
        Assert.Contains("x1 = 20.000", output);
        Assert.Contains("x2 = 60.000", output);
    }
}
