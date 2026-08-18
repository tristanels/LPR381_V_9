using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>A variable that came out fractional, and by how much.</summary>
/// <param name="Variable">The decision variable.</param>
/// <param name="Index">Its position in the model, used for the smallest-subscript tie-break.</param>
/// <param name="Value">Its value in the table.</param>
/// <param name="Fraction">The fractional part, always in [0, 1).</param>
public sealed record FractionalVariable(Variable Variable, int Index, double Value, double Fraction)
{
    /// <summary>Distance from 0.5 - the smaller this is, the better a branching candidate.</summary>
    public double DistanceFromHalf => Math.Abs(Fraction - 0.5);
}

/// <summary>
/// The rules Branch &amp; Bound and the Cutting Plane share for picking what to work on next
/// (study notes, Section 4 Step 1 and Section 5 Step 1): take the variable whose fractional part is
/// closest to 0.5, and break a tie by taking the smallest subscript.
/// </summary>
public static class IntegerRules
{
    /// <summary>Every int/bin variable that did not come out whole.</summary>
    public static IReadOnlyList<FractionalVariable> FractionalVariables(
        LinearModel model,
        CanonicalForm canonicalForm,
        Tableau tableau)
    {
        IReadOnlyDictionary<string, double> solution = canonicalForm.ReadSolution(tableau);
        List<FractionalVariable> fractional = [];

        for (int i = 0; i < model.Variables.Count; i++)
        {
            Variable variable = model.Variables[i];
            if (!variable.IsDiscrete)
            {
                continue;
            }

            double value = solution[variable.Name];
            if (!Num.IsInteger(value))
            {
                fractional.Add(new FractionalVariable(variable, i, value, Num.FractionalPart(value)));
            }
        }

        return fractional;
    }

    /// <summary>True when every int/bin variable came out whole, so the node is a candidate.</summary>
    public static bool IsIntegerFeasible(LinearModel model, CanonicalForm canonicalForm, Tableau tableau) =>
        FractionalVariables(model, canonicalForm, tableau).Count == 0;

    /// <summary>
    /// The variable to branch on (or to cut on): fractional part closest to 0.5, smallest subscript
    /// wins a tie. Returns null when nothing is fractional.
    /// </summary>
    public static FractionalVariable? SelectBranchingVariable(
        LinearModel model,
        CanonicalForm canonicalForm,
        Tableau tableau) =>
        FractionalVariables(model, canonicalForm, tableau)
            .OrderBy(candidate => candidate.DistanceFromHalf)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();

    /// <summary>
    /// The row a basic variable sits in - "the row where its 1 is", which is the row the Cutting Plane
    /// builds its cut from.
    /// </summary>
    public static int RowOf(Tableau tableau, int column)
    {
        for (int row = Tableau.FirstConstraintRow; row < tableau.RowCount; row++)
        {
            if (tableau.Basis[row] == column)
            {
                return row;
            }
        }

        return -1;
    }

    /// <summary>
    /// A unique name for a slack or excess column added to an existing table. Counts what is already
    /// there so a new bound or cut never reuses a name.
    /// </summary>
    public static string NextLogicalName(Tableau tableau, int decisionColumnCount, bool isSlack) =>
        (isSlack ? "s" : "e") + (tableau.RhsColumn - decisionColumnCount + 1);
}
