using LPR381.Core.Algorithms;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Sensitivity;

/// <summary>
/// Shared plumbing for the sensitivity operations: it builds the <see cref="SensitivityContext"/>,
/// turns a missing or unusable result into a readable message, and keeps a thrown exception from
/// reaching the menu.
/// </summary>
public abstract class SensitivityOperation : ISensitivityOperation
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    public string Execute(SolveResult solved, ISensitivityInput input)
    {
        ArgumentNullException.ThrowIfNull(solved);
        ArgumentNullException.ThrowIfNull(input);

        SensitivityContext? context = SensitivityContext.From(solved);

        if (context is null)
        {
            return
                "Sensitivity analysis works off an optimal simplex table.\n" +
                "Solve the model with the Primal Simplex or the Revised Primal Simplex first. " +
                "Branch & Bound and the Cutting Plane end on a table with extra bound or cut rows, " +
                "and Knapsack B&B never builds a table at all, so none of those can be analysed directly.";
        }

        try
        {
            return Run(context, input);
        }
        catch (Exception error)
        {
            return $"{Name} could not be completed: {error.Message}";
        }
    }

    protected abstract string Run(SensitivityContext context, ISensitivityInput input);

    /// <summary>Asks the user to pick one of the non-basic columns.</summary>
    protected static int PickNonBasicColumn(SensitivityContext context, ISensitivityInput input, string prompt)
    {
        IReadOnlyList<int> columns = context.NonBasicColumns;
        if (columns.Count == 0)
        {
            return -1;
        }

        int choice = input.ReadChoice(prompt, columns.Select(context.ColumnName).ToArray());
        return choice < 0 ? -1 : columns[choice];
    }

    /// <summary>Asks the user to pick one of the basic columns.</summary>
    protected static int PickBasicColumn(SensitivityContext context, ISensitivityInput input, string prompt)
    {
        IReadOnlyList<int> columns = context.BasicColumns;
        int choice = input.ReadChoice(prompt, columns.Select(context.ColumnName).ToArray());
        return choice < 0 ? -1 : columns[choice];
    }

    /// <summary>Asks the user to pick one of the constraint rows.</summary>
    protected static int PickRow(SensitivityContext context, ISensitivityInput input, string prompt)
    {
        string[] options = Enumerable.Range(0, context.ConstraintCount)
            .Select(context.DescribeRow)
            .ToArray();

        return input.ReadChoice(prompt, options);
    }

    protected const string Cancelled = "Cancelled - nothing was changed.";

    /// <summary>
    /// The allowable range for a value that has to keep a set of expressions non-negative (max) or
    /// non-positive (min). Returns the largest decrease and increase that keep the basis optimal.
    /// </summary>
    protected static (double Decrease, double Increase) AllowableChange(
        IEnumerable<(double Current, double Rate)> constraints,
        ObjectiveSense sense)
    {
        double decrease = double.PositiveInfinity;
        double increase = double.PositiveInfinity;

        foreach ((double current, double rate) in constraints)
        {
            if (Num.IsZero(rate))
            {
                continue;
            }

            // current + delta * rate must stay >= 0 for a max problem, <= 0 for a min problem.
            double limit = -current / rate;
            bool limitsIncrease = sense == ObjectiveSense.Max ? rate < 0 : rate > 0;

            if (limitsIncrease)
            {
                increase = Math.Min(increase, Math.Abs(limit));
            }
            else
            {
                decrease = Math.Min(decrease, Math.Abs(limit));
            }
        }

        return (decrease, increase);
    }
}
