using System.Text;
using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// How one decision variable of the original model is represented by tableau columns.
/// A normal variable is one column with multiplier +1; an <c>urs</c> variable becomes two columns
/// (x+ and x-) with multipliers +1 and -1.
/// </summary>
public sealed record SubstitutionTerm(int Column, double Multiplier);

/// <summary>Maps a decision variable of the input model onto the columns of the tableau.</summary>
public sealed class VariableSubstitution
{
    public VariableSubstitution(string variableName, IReadOnlyList<SubstitutionTerm> terms)
    {
        VariableName = variableName;
        Terms = terms;
    }

    public string VariableName { get; }

    public IReadOnlyList<SubstitutionTerm> Terms { get; }
}

/// <summary>
/// The starting simplex table plus a plain-language record of how it was built. The brief requires
/// the canonical form to be displayed before the iterations, so the working is kept, not just the numbers.
/// </summary>
public sealed class CanonicalForm
{
    public CanonicalForm(
        LinearModel model,
        Tableau initialTableau,
        IReadOnlyList<VariableSubstitution> substitutions,
        IReadOnlyList<string> steps)
    {
        Model = model;
        InitialTableau = initialTableau;
        Substitutions = substitutions;
        Steps = steps;
    }

    public LinearModel Model { get; }

    /// <summary>The starting table, labelled "T-0".</summary>
    public Tableau InitialTableau { get; }

    public IReadOnlyList<VariableSubstitution> Substitutions { get; }

    /// <summary>One line per conversion decision taken, in the order they were applied.</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>
    /// True when a constraint produced a negative RHS (a >= constraint, or a <= with a negative RHS).
    /// The notes say to run the Dual Simplex first in that case.
    /// </summary>
    public bool RequiresDualSimplex => InitialTableau.HasNegativeRightHandSide();

    /// <summary>
    /// Translates the columns of a solved table back into values for the decision variables the
    /// user actually wrote in the input file.
    /// </summary>
    public IReadOnlyDictionary<string, double> ReadSolution(Tableau tableau)
    {
        IReadOnlyDictionary<string, double> columnValues = tableau.ReadColumnValues();
        Dictionary<string, double> solution = [];

        foreach (VariableSubstitution substitution in Substitutions)
        {
            double value = 0.0;
            foreach (SubstitutionTerm term in substitution.Terms)
            {
                value += term.Multiplier * columnValues[tableau.ColumnNames[term.Column]];
            }

            solution[substitution.VariableName] = value;
        }

        return solution;
    }

    /// <summary>Writes the canonical form out as equations, one per row of the starting table.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine("CANONICAL FORM");
        text.AppendLine();

        foreach (string step in Steps)
        {
            text.AppendLine("  - " + step);
        }

        text.AppendLine();

        Tableau tableau = InitialTableau;
        for (int row = 0; row < tableau.RowCount; row++)
        {
            string prefix = row == Tableau.ObjectiveRow ? "  z   : z" : $"  {tableau.RowLabels[row],-4}:";
            text.Append(prefix);
            text.Append(' ');
            text.Append(FormatRow(tableau, row, includeLeadingSign: row == Tableau.ObjectiveRow));
            text.Append(" = ");
            text.AppendLine(Num.Format(tableau.Rhs(row)));
        }

        return text.ToString();
    }

    private static string FormatRow(Tableau tableau, int row, bool includeLeadingSign)
    {
        var text = new StringBuilder();
        bool first = true;

        for (int column = 0; column < tableau.RhsColumn; column++)
        {
            double coefficient = tableau[row, column];
            if (Num.IsZero(coefficient))
            {
                continue;
            }

            if (!first || includeLeadingSign)
            {
                text.Append(Num.IsNegative(coefficient) ? " - " : " + ");
            }
            else if (Num.IsNegative(coefficient))
            {
                text.Append('-');
            }

            text.Append(Num.Format(Math.Abs(coefficient)));
            text.Append(tableau.ColumnNames[column]);
            first = false;
        }

        return first && !includeLeadingSign ? "0" : text.ToString();
    }
}
