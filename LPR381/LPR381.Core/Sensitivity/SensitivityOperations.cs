using LPR381.Core.Algorithms;

namespace LPR381.Core.Sensitivity;

/// <summary>
/// One sensitivity analysis operation from the brief. They all run against an already solved model,
/// which is why <see cref="Execute"/> takes the <see cref="SolveResult"/> the user just produced.
/// </summary>
public interface ISensitivityOperation
{
    string Name { get; }

    string Description { get; }

    bool IsImplemented { get; }

    /// <summary>Returns the text to show on screen and write to the output file.</summary>
    string Execute(SolveResult solved);
}

/// <summary>
/// A sensitivity operation that is still to be written. It appears in the menu with an honest
/// message so the structure is in place and each team member can fill in their own operation.
/// </summary>
public sealed class PlaceholderSensitivityOperation : ISensitivityOperation
{
    public PlaceholderSensitivityOperation(string name, string description, string outstandingWork)
    {
        Name = name;
        Description = description;
        OutstandingWork = outstandingWork;
    }

    public string Name { get; }

    public string Description { get; }

    /// <summary>What still has to be built for this operation.</summary>
    public string OutstandingWork { get; }

    public bool IsImplemented => false;

    public string Execute(SolveResult solved) =>
        $"{Name} is not implemented yet.{Environment.NewLine}Still to build: {OutstandingWork}";
}

/// <summary>
/// Every sensitivity analysis operation the brief asks for, in the order it lists them. The
/// sensitivity menu is generated from this list.
/// </summary>
public static class SensitivityRegistry
{
    public static IReadOnlyList<ISensitivityOperation> All { get; } =
    [
        new PlaceholderSensitivityOperation(
            "Range of a Non-Basic Variable",
            "How far the objective coefficient of a non-basic variable may move before it enters the basis.",
            "the allowable increase/decrease from the reduced cost of that column."),

        new PlaceholderSensitivityOperation(
            "Change a Non-Basic Variable",
            "Apply a new objective coefficient to a non-basic variable and re-optimise.",
            "applying the new coefficient to the z row and re-running the simplex from the current table."),

        new PlaceholderSensitivityOperation(
            "Range of a Basic Variable",
            "How far the objective coefficient of a basic variable may move before the basis changes.",
            "the ratio test over that variable's row against the z row values."),

        new PlaceholderSensitivityOperation(
            "Change a Basic Variable",
            "Apply a new objective coefficient to a basic variable and re-optimise.",
            "rebuilding the z row with CBV*B-inverse after the change, then re-optimising."),

        new PlaceholderSensitivityOperation(
            "Range of a constraint RHS",
            "How far a right-hand-side value may move while the current basis stays optimal.",
            "B-inverse times the RHS, with the ratio test that gives the allowable increase and decrease."),

        new PlaceholderSensitivityOperation(
            "Change a constraint RHS",
            "Apply a new right-hand-side value and re-optimise.",
            "recomputing B-inverse*b and re-solving with the Dual Simplex when the new RHS goes negative."),

        new PlaceholderSensitivityOperation(
            "Range of a variable in a Non-Basic Variable column",
            "How far a technological coefficient in a non-basic column may move.",
            "the price out of the changed column against CBV*B-inverse."),

        new PlaceholderSensitivityOperation(
            "Change a variable in a Non-Basic Variable column",
            "Apply a new technological coefficient in a non-basic column and re-optimise.",
            "re-pricing that column and re-optimising if it becomes attractive."),

        new PlaceholderSensitivityOperation(
            "Add a new activity",
            "Add a new decision variable to the optimal solution.",
            "pricing out the new column with CBV*B-inverse and bringing it in if it improves z."),

        new PlaceholderSensitivityOperation(
            "Add a new constraint",
            "Add a new constraint to the optimal solution.",
            "appending the row, restoring canonical form against the current basis, and re-solving with the Dual Simplex."),

        new PlaceholderSensitivityOperation(
            "Display the shadow prices",
            "The worth of one more unit of each constrained resource.",
            "reading CBV*B-inverse off the optimal table (the z row values under the slack columns)."),

        new PlaceholderSensitivityOperation(
            "Duality",
            "Build the dual model, solve it, and check for strong or weak duality.",
            "transposing the model into its dual, solving it, comparing z and w, and reporting the duality gap.")
    ];
}
