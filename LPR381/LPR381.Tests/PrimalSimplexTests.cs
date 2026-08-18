using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Tests;

/// <summary>
/// Works the Santa's Workshop LP through by hand and checks the program agrees, table by table.
/// This is the regression net for the pivot arithmetic that every later algorithm builds on.
/// </summary>
public class PrimalSimplexTests
{
    private const string Santa = CanonicalFormBuilderTests.SantaModel;

    [Fact]
    public void Pivot_builds_the_next_table_the_way_the_notes_do()
    {
        Tableau t0 = CanonicalFormBuilder.Build(ModelFileParser.ParseText(Santa)).InitialTableau;

        // Pivot column x1 (largest negative in the z row), pivot row 3 (smallest positive ratio 40).
        Tableau t1 = PivotEngine.Pivot(t0, pivotRow: 3, pivotColumn: 0, "T-1");

        // z row: 0  -2  0  0  3 | 120
        Assert.Equal(0, t1[0, 0], 9);
        Assert.Equal(-2, t1[0, 1], 9);
        Assert.Equal(3, t1[0, 4], 9);
        Assert.Equal(120, t1.ObjectiveValue, 9);

        // Row 1: 0  1  1  0  -2 | 20
        Assert.Equal(0, t1[1, 0], 9);
        Assert.Equal(1, t1[1, 1], 9);
        Assert.Equal(-2, t1[1, 4], 9);
        Assert.Equal(20, t1.Rhs(1), 9);

        // x1 has taken s3's place in the basis of row 3.
        Assert.Equal("x1", t1.ColumnNames[t1.Basis[3]]);

        // The original table is untouched, which is what lets every iteration be displayed.
        Assert.Equal(-3, t0[0, 0], 9);
    }

    [Fact]
    public void Solves_the_Santa_workshop_LP()
    {
        SolveResult result = Solve(Santa);

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(180, result.ObjectiveValue, 3);
        Assert.Equal(20, result.VariableValues!["x1"], 3);
        Assert.Equal(60, result.VariableValues["x2"], 3);

        // Three pivots plus the final optimal table.
        Assert.Equal(4, result.Steps.Count);
        Assert.Equal("T-0", result.Steps[0].Tableau.Label);
        Assert.Equal("T-3", result.Steps[^1].Tableau.Label);
        Assert.False(result.Steps[^1].HasPivot);
    }

    [Fact]
    public void Every_pivot_step_records_its_row_column_and_ratios()
    {
        SolveResult result = Solve(Santa);
        IterationStep first = result.Steps[0];

        Assert.True(first.HasPivot);
        Assert.Equal(3, first.PivotRow);
        Assert.Equal(0, first.PivotColumn);
        Assert.Equal(50, first.Ratios![1], 9);
        Assert.Equal(80, first.Ratios[2], 9);
        Assert.Equal(40, first.Ratios[3], 9);
        Assert.Contains("Pivot column x1", first.Narrative);
    }

    [Fact]
    public void Solves_a_minimisation_that_needs_the_dual_simplex_first()
    {
        // min z = 2x1 + 3x2, x1 + x2 >= 10, x1 >= 3. The cheapest way to cover 10 units is all x1.
        SolveResult result = Solve("""
            min +2 +3
            +1 +1 >=10
            +1 +0 >=3
            + +
            """);

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(20, result.ObjectiveValue, 3);
        Assert.Equal(10, result.VariableValues!["x1"], 3);
        Assert.Equal(0, result.VariableValues["x2"], 3);
        Assert.Contains(result.Steps, step => step.Heading.Contains("Dual Simplex"));
    }

    [Fact]
    public void Reports_an_unbounded_model_instead_of_looping()
    {
        SolveResult result = Solve("""
            max +1 +1
            +1 -1 <=1
            + +
            """);

        Assert.Equal(SolutionStatus.Unbounded, result.Status);
        Assert.Contains("UNBOUNDED", result.Message);
    }

    [Fact]
    public void Reports_an_infeasible_model()
    {
        // x1 <= 2 and x1 >= 5 cannot both hold.
        SolveResult result = Solve("""
            max +1
            +1 <=2
            +1 >=5
            +
            """);

        Assert.Equal(SolutionStatus.Infeasible, result.Status);
        Assert.Contains("INFEASIBLE", result.Message);
    }

    [Fact]
    public void Solves_the_relaxed_knapsack_from_the_brief_and_flags_the_fractional_variable()
    {
        SolveResult result = Solve("""
            max +2 +3 +3 +5 +2 +4
            +11 +8 +6 +14 +10 +10 <=40
            bin bin bin bin bin bin
            """);

        Assert.Equal(SolutionStatus.Optimal, result.Status);

        // Filling the bag by z/c: x3, x6, x2, x4 whole, then 2/10 of x5.
        Assert.Equal(15.4, result.ObjectiveValue, 3);
        Assert.Equal(0.2, result.VariableValues!["x5"], 3);
        Assert.Contains("RELAXED", result.Message);
        Assert.Contains(result.Steps, step => step.Narrative.Contains("Branch & Bound"));
    }

    [Fact]
    public void Reads_an_unrestricted_variable_back_as_a_negative_value()
    {
        // max z = x1 + x2 with x1 + x2 <= 4 and x2 >= 2 is best served by pushing x2 negative:
        // max -x2 subject to x2 >= -3 gives x2 = -3.
        SolveResult result = Solve("""
            max +1 -1
            +1 +1 <=4
            +0 +1 >=-3
            + urs
            """);

        Assert.Equal(SolutionStatus.Optimal, result.Status);
        Assert.Equal(-3, result.VariableValues!["x2"], 3);
        Assert.Equal(7, result.VariableValues["x1"], 3);
        Assert.Equal(10, result.ObjectiveValue, 3);
    }

    private static SolveResult Solve(string modelText)
    {
        LinearModel model = ModelFileParser.ParseText(modelText);
        return new PrimalSimplexSolver().Solve(model);
    }
}
