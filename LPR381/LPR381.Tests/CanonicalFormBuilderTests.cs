using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Tests;

/// <summary>
/// Checks the canonical form against the rules in Section 1 and Section 3 of the study notes.
/// </summary>
public class CanonicalFormBuilderTests
{
    /// <summary>The Santa's Workshop LP the module's Excel workbook works through.</summary>
    public const string SantaModel = """
        max +3 +2
        +2 +1 <=100
        +1 +1 <=80
        +1 +0 <=40
        + +
        """;

    [Fact]
    public void Moves_the_objective_to_the_left_and_negates_it()
    {
        CanonicalForm form = Build(SantaModel);
        Tableau tableau = form.InitialTableau;

        // max z = 3x1 + 2x2  ->  z - 3x1 - 2x2 = 0
        Assert.Equal(-3, tableau[Tableau.ObjectiveRow, 0], 9);
        Assert.Equal(-2, tableau[Tableau.ObjectiveRow, 1], 9);
        Assert.Equal(0, tableau.ObjectiveValue, 9);
        Assert.Equal(["x1", "x2", "s1", "s2", "s3", "rhs"], tableau.ColumnNames);
        Assert.Equal(["z", "1", "2", "3"], tableau.RowLabels);
    }

    [Fact]
    public void Adds_a_slack_to_every_less_than_or_equal_row_and_makes_it_basic()
    {
        Tableau tableau = Build(SantaModel).InitialTableau;

        Assert.Equal(1, tableau[1, 2], 9);
        Assert.Equal(100, tableau.Rhs(1), 9);
        Assert.Equal("s1", tableau.ColumnNames[tableau.Basis[1]]);
        Assert.Equal("s3", tableau.ColumnNames[tableau.Basis[3]]);
        Assert.False(tableau.HasNegativeRightHandSide());
    }

    [Fact]
    public void Subtracts_an_excess_from_a_greater_than_row_then_multiplies_the_row_by_minus_one()
    {
        Tableau tableau = Build("""
            min +2 +3
            +1 +1 >=10
            + +
            """).InitialTableau;

        // x1 + x2 - e1 = 10, multiplied by -1  ->  -x1 - x2 + e1 = -10
        Assert.Equal(-1, tableau[1, 0], 9);
        Assert.Equal(-1, tableau[1, 1], 9);
        Assert.Equal(1, tableau[1, 2], 9);
        Assert.Equal(-10, tableau.Rhs(1), 9);
        Assert.Equal("e1", tableau.ColumnNames[tableau.Basis[1]]);
        Assert.True(tableau.HasNegativeRightHandSide());
    }

    [Fact]
    public void Splits_an_equality_into_a_less_than_row_and_a_greater_than_row()
    {
        CanonicalForm form = Build("""
            max +1 +1
            +1 +1 =5
            + +
            """);

        Assert.Equal(["z", "1'", "1\""], form.InitialTableau.RowLabels);
        Assert.Equal(5, form.InitialTableau.Rhs(1), 9);
        Assert.Equal(-5, form.InitialTableau.Rhs(2), 9);
        Assert.True(form.RequiresDualSimplex);
    }

    [Fact]
    public void Gives_every_binary_variable_its_upper_bound_row()
    {
        CanonicalForm form = Build("""
            max +2 +3
            +11 +8 <=40
            bin bin
            """);

        // One user constraint plus one x <= 1 row per binary variable.
        Assert.Equal(["z", "1", "b1", "b2"], form.InitialTableau.RowLabels);
        Assert.Equal(1, form.InitialTableau.Rhs(2), 9);
        Assert.Equal(1, form.InitialTableau[2, 0], 9);
    }

    [Fact]
    public void Substitutes_an_unrestricted_variable_into_two_columns()
    {
        CanonicalForm form = Build("""
            max +3 +2
            +1 +1 <=10
            + urs
            """);

        Assert.Equal(["x1", "x2+", "x2-", "s1", "rhs"], form.InitialTableau.ColumnNames);
        Assert.Equal(-2, form.InitialTableau[Tableau.ObjectiveRow, 1], 9);
        Assert.Equal(2, form.InitialTableau[Tableau.ObjectiveRow, 2], 9);
    }

    [Fact]
    public void Substitutes_a_negative_variable_and_reads_the_answer_back_as_negative()
    {
        CanonicalForm form = Build("""
            max +1 -2
            +1 -1 <=10
            + -
            """);

        Assert.Equal(["x1", "x2'", "s1", "rhs"], form.InitialTableau.ColumnNames);

        // x2 = -x2', so the objective coefficient of the substituted column flips sign:
        // z row holds -c, therefore -(-(-2)) = -2.
        Assert.Equal(-2, form.InitialTableau[Tableau.ObjectiveRow, 1], 9);
    }

    private static CanonicalForm Build(string modelText) =>
        CanonicalFormBuilder.Build(ModelFileParser.ParseText(modelText));
}
