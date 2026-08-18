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
/// Where one row of the canonical form came from. Sensitivity analysis needs this to map a table row
/// back to the constraint the user wrote, and to get the sign right for a row that was negated.
/// </summary>
/// <param name="Label">Row label in the table, e.g. "1", "2'" or "b3".</param>
/// <param name="SourceConstraintIndex">Index into the model's constraints, or -1 for a generated bound row.</param>
/// <param name="OriginalRelation">The relation the user wrote.</param>
/// <param name="Negated">True when the row was multiplied by -1 (every &gt;= row is).</param>
/// <param name="LogicalColumn">Column of the slack or excess that starts off basic in this row.</param>
public sealed record CanonicalRow(
    string Label,
    int SourceConstraintIndex,
    Relation OriginalRelation,
    bool Negated,
    int LogicalColumn);

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
        IReadOnlyList<string> steps,
        IReadOnlyList<CanonicalRow> rows,
        int decisionColumnCount)
    {
        Model = model;
        InitialTableau = initialTableau;
        Substitutions = substitutions;
        Steps = steps;
        Rows = rows;
        DecisionColumnCount = decisionColumnCount;
    }

    /// <summary>One entry per constraint row of the table, in table order (row 1 is Rows[0]).</summary>
    public IReadOnlyList<CanonicalRow> Rows { get; }

    /// <summary>How many of the leading columns belong to decision variables (urs variables take two).</summary>
    public int DecisionColumnCount { get; }

    /// <summary>The canonical row for a table row index (row 1 of the table is the first constraint).</summary>
    public CanonicalRow RowOf(int tableauRow) => Rows[tableauRow - Tableau.FirstConstraintRow];

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

    /// <summary>
    /// The tableau column that carries a decision variable. Branch &amp; Bound and the Cutting Plane use
    /// this to add a bound on a variable; both only ever branch on int/bin variables, which are never
    /// split across two columns the way an urs variable is.
    /// </summary>
    public int ColumnOf(string variableName) =>
        Substitutions.First(s => s.VariableName == variableName).Terms[0].Column;

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
