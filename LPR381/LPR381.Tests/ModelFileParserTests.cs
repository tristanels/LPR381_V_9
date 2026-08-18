using LPR381.Core.IO;
using LPR381.Core.Models;

namespace LPR381.Tests;

/// <summary>Covers the input file format from the brief, including the errors a user is likely to make.</summary>
public class ModelFileParserTests
{
    private const string KnapsackFile = """
        max +2 +3 +3 +5 +2 +4
        +11 +8 +6 +14 +10 +10 <=40
        bin bin bin bin bin bin
        """;

    [Fact]
    public void Parses_the_knapsack_example_from_the_brief()
    {
        LinearModel model = ModelFileParser.ParseText(KnapsackFile);

        Assert.Equal(ObjectiveSense.Max, model.Sense);
        Assert.Equal(6, model.Variables.Count);
        Assert.Single(model.Constraints);
        Assert.Equal([2, 3, 3, 5, 2, 4], model.Variables.Select(v => v.ObjectiveCoefficient));
        Assert.All(model.Variables, v => Assert.Equal(SignRestriction.Binary, v.SignRestriction));

        Constraint constraint = model.Constraints[0];
        Assert.Equal([11, 8, 6, 14, 10, 10], constraint.Coefficients);
        Assert.Equal(Relation.LessOrEqual, constraint.Relation);
        Assert.Equal(40, constraint.RightHandSide);
        Assert.True(model.IsBinaryModel);
    }

    [Fact]
    public void Accepts_signs_and_relations_that_are_spaced_out()
    {
        LinearModel model = ModelFileParser.ParseText("""
            MAX + 3 - 2
            + 2 + 1 <= 100
            + +
            """);

        Assert.Equal(ObjectiveSense.Max, model.Sense);
        Assert.Equal([3, -2], model.Variables.Select(v => v.ObjectiveCoefficient));
        Assert.Equal(100, model.Constraints[0].RightHandSide);
    }

    [Theory]
    [InlineData(">=", Relation.GreaterOrEqual)]
    [InlineData("<=", Relation.LessOrEqual)]
    [InlineData("=", Relation.Equal)]
    public void Reads_every_relation(string symbol, Relation expected)
    {
        LinearModel model = ModelFileParser.ParseText($"""
            min +1 +1
            +1 +1 {symbol}5
            + +
            """);

        Assert.Equal(expected, model.Constraints[0].Relation);
    }

    [Fact]
    public void Reads_every_sign_restriction()
    {
        LinearModel model = ModelFileParser.ParseText("""
            max +1 +1 +1 +1 +1
            +1 +1 +1 +1 +1 <=5
            + - urs int bin
            """);

        Assert.Equal(
            [
                SignRestriction.Positive,
                SignRestriction.Negative,
                SignRestriction.Unrestricted,
                SignRestriction.Integer,
                SignRestriction.Binary
            ],
            model.Variables.Select(v => v.SignRestriction));
    }

    [Fact]
    public void Rejects_a_constraint_with_the_wrong_number_of_coefficients()
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() => ModelFileParser.ParseText("""
            max +3 +2
            +2 +1 +4 <=100
            + +
            """));

        Assert.Equal(2, error.LineNumber);
        Assert.Contains("3 technological coefficient", error.Message);
    }

    [Fact]
    public void Rejects_a_first_line_that_is_not_max_or_min()
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() => ModelFileParser.ParseText("""
            maximise +3 +2
            +2 +1 <=100
            + +
            """));

        Assert.Equal(1, error.LineNumber);
        Assert.Contains("max", error.Message);
    }

    [Fact]
    public void Rejects_a_constraint_without_a_relation()
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() => ModelFileParser.ParseText("""
            max +3 +2
            +2 +1 100
            + +
            """));

        Assert.Equal(2, error.LineNumber);
        Assert.Contains("relation", error.Message);
    }

    [Fact]
    public void Rejects_a_missing_sign_restriction_line()
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() => ModelFileParser.ParseText("""
            max +3 +2
            +2 +1 <=100
            """));

        Assert.Contains("sign restrictions", error.Message);
    }

    [Fact]
    public void Rejects_the_wrong_number_of_sign_restrictions()
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() => ModelFileParser.ParseText("""
            max +3 +2
            +2 +1 <=100
            + + +
            """));

        Assert.Contains("3 sign restriction", error.Message);
    }

    [Fact]
    public void Rejects_a_model_with_no_constraints()
    {
        ModelParseException error = Assert.Throws<ModelParseException>(() => ModelFileParser.ParseText("""
            max +3 +2
            + +
            """));

        Assert.Contains("no constraints", error.Message);
    }

    [Fact]
    public void Ignores_blank_lines_and_comments_but_keeps_the_real_line_numbers()
    {
        LinearModel model = ModelFileParser.ParseText("""
            # Santa's Workshop
            max +3 +2

            +2 +1 <=100
            +1 +1 <=80   // carpentry
            +1 +0 <=40
            + +
            """);

        Assert.Equal(3, model.Constraints.Count);
    }
}
