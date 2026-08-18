using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Sensitivity;
using LPR381.Core.Simplex;

namespace LPR381.Tests;

/// <summary>
/// Sensitivity analysis, checked against the Santa's Workshop LP the module's Excel workbook
/// works through. Its optimal table is z row = [0, 0, 1, 1, 0 | 180], so the shadow prices are
/// 1, 1 and 0.
/// </summary>
public class SensitivityTests
{
    private const string Santa = CanonicalFormBuilderTests.SantaModel;

    /// <summary>Feeds scripted answers to an operation instead of asking a person.</summary>
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

    private static string Run(ISensitivityOperation operation, int[] choices, double[] numbers) =>
        operation.Execute(SolveSanta(), new ScriptedInput(choices, numbers));

    [Fact]
    public void Shadow_prices_come_off_the_z_row_under_the_slack_columns()
    {
        SensitivityContext context = SensitivityContext.From(SolveSanta())!;

        Assert.Equal([1.0, 1.0, 0.0], context.ConstraintShadowPrices.Select(p => Math.Round(p, 6)));
    }

    [Fact]
    public void Shadow_price_report_marks_the_non_binding_constraint()
    {
        string output = Run(new ShadowPriceOperation(), [], []);

        Assert.Contains("Shadow prices", output);
        Assert.Contains("not binding", output);
    }

    [Fact]
    public void B_inverse_is_read_straight_out_of_the_optimal_table()
    {
        SensitivityContext context = SensitivityContext.From(SolveSanta())!;

        // B-inverse times the original b must give the basic variable values back.
        double[] rhs = BasisMatrix.OriginalRhs(context.Form);
        double[] basicValues = BasisMatrix.Multiply(context.Inverse, rhs);

        for (int row = 0; row < basicValues.Length; row++)
        {
            Assert.Equal(context.Tableau.Rhs(row + Tableau.FirstConstraintRow), basicValues[row], 6);
        }
    }

    [Fact]
    public void Range_of_a_basic_variable_brackets_its_current_coefficient()
    {
        // x1 is basic with coefficient 3 in the Santa LP.
        string output = Run(new BasicVariableRangeOperation(), [0], []);

        Assert.Contains("Range of the basic variable", output);
        Assert.Contains("Allowable increase", output);
        Assert.Contains("Allowable decrease", output);
    }

    [Fact]
    public void Changing_a_basic_variable_coefficient_moves_z_and_re_optimises()
    {
        // Raise the profit on x1 from 3 to 10: it becomes worth making only soldiers.
        string output = Run(new BasicVariableChangeOperation(), [0], [10.0]);

        Assert.Contains("New coefficient = 10.000", output);
        Assert.Contains("New solution:", output);
    }

    [Fact]
    public void Rhs_range_is_the_range_over_which_the_shadow_price_holds()
    {
        string output = Run(new RhsRangeOperation(), [0], []);

        Assert.Contains("Range of the right-hand-side", output);
        Assert.Contains("Shadow price", output);
        Assert.Contains("b* = B-inverse x b must stay non-negative", output);
    }

    [Fact]
    public void Changing_an_rhs_moves_z_by_the_shadow_price()
    {
        // Constraint 1 has a shadow price of 1, so one more unit of it is worth exactly 1 more z.
        string output = Run(new RhsChangeOperation(), [0], [101.0]);

        Assert.Contains("New right-hand-side = 101.000", output);
        Assert.Contains("z = 181.000", output);
    }

    [Fact]
    public void Adding_an_activity_that_does_not_pay_leaves_the_answer_alone()
    {
        // A new product worth 1 that uses 1 unit of each resource cannot pay: the shadow prices
        // already add up to 2 for those rows.
        string output = Run(new AddActivityOperation(), [], [1.0, 1.0, 1.0, 1.0]);

        Assert.Contains("does not pay at the current shadow prices", output);
    }

    [Fact]
    public void Adding_an_activity_that_pays_brings_it_into_the_basis()
    {
        // Worth 10 for one unit of each resource: that beats the shadow prices, so it enters.
        string output = Run(new AddActivityOperation(), [], [10.0, 1.0, 1.0, 1.0]);

        Assert.Contains("prices out as attractive", output);
        Assert.Contains("New solution:", output);
    }

    [Fact]
    public void Adding_a_constraint_that_the_solution_breaks_triggers_the_dual_simplex()
    {
        // x1 + x2 <= 50 cuts off the current answer of x1 = 20, x2 = 60.
        string output = Run(new AddConstraintOperation(), [0], [1.0, 1.0, 50.0]);

        Assert.Contains("The current solution breaks it", output);
        Assert.Contains("Dual Simplex", output);
        Assert.Contains("New solution:", output);
    }

    [Fact]
    public void Adding_a_constraint_that_already_holds_changes_nothing()
    {
        string output = Run(new AddConstraintOperation(), [0], [1.0, 1.0, 500.0]);

        Assert.Contains("already satisfies it", output);
    }

    [Fact]
    public void Duality_gives_the_dual_the_same_objective_value()
    {
        string output = Run(new DualityOperation(), [], []);

        Assert.Contains("DUAL MODEL", output);
        Assert.Contains("min w", output.Replace("min z", "min w"));
        Assert.Contains("STRONG DUALITY", output);
        Assert.Contains("Duality gap  = 0.000", output);
    }

    [Fact]
    public void The_dual_of_a_max_model_is_a_min_model_with_transposed_constraints()
    {
        LinearModel primal = ModelFileParser.ParseText(Santa);
        (LinearModel dual, _) = DualityOperation.BuildDual(primal);

        Assert.Equal(ObjectiveSense.Min, dual.Sense);

        // Three primal constraints become three dual variables; two primal variables become two rows.
        Assert.Equal(3, dual.Variables.Count);
        Assert.Equal(2, dual.Constraints.Count);
        Assert.Equal([100, 80, 40], dual.Variables.Select(v => v.ObjectiveCoefficient));
        Assert.Equal([2, 1, 1], dual.Constraints[0].Coefficients);
        Assert.Equal(Relation.GreaterOrEqual, dual.Constraints[0].Relation);
        Assert.Equal(3, dual.Constraints[0].RightHandSide);
    }

    [Fact]
    public void The_dual_solves_to_the_same_objective_value_as_the_primal()
    {
        LinearModel primal = ModelFileParser.ParseText(Santa);
        (LinearModel dual, _) = DualityOperation.BuildDual(primal);

        SolveResult primalResult = new PrimalSimplexSolver().Solve(primal);
        SolveResult dualResult = new PrimalSimplexSolver().Solve(dual);

        Assert.Equal(SolutionStatus.Optimal, dualResult.Status);
        Assert.Equal(primalResult.ObjectiveValue, dualResult.ObjectiveValue, 6);

        // The optimal dual variables are the primal shadow prices.
        SensitivityContext context = SensitivityContext.From(primalResult)!;
        Assert.Equal(context.ConstraintShadowPrices[0], dualResult.VariableValues!["y1"], 6);
        Assert.Equal(context.ConstraintShadowPrices[1], dualResult.VariableValues["y2"], 6);
    }

    [Fact]
    public void Sensitivity_turns_away_a_result_it_cannot_analyse()
    {
        SolveResult knapsack = new KnapsackBranchAndBoundSolver().Solve(ModelFileParser.ParseText("""
            max +2 +3
            +11 +8 <=40
            bin bin
            """));

        string output = new ShadowPriceOperation().Execute(knapsack, new ScriptedInput([], []));

        Assert.Contains("works off an optimal simplex table", output);
    }

    [Fact]
    public void A_cancelled_operation_says_so_instead_of_crashing()
    {
        string output = Run(new BasicVariableChangeOperation(), [0], []);

        Assert.Contains("Cancelled", output);
    }
}
