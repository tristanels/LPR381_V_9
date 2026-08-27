using System.Text;
using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Sensitivity;

/// <summary>
/// How a sensitivity operation asks the user for the input it needs. The console implements this, so
/// LPR381.Core stays free of Console calls and the operations stay testable.
/// </summary>
public interface ISensitivityInput
{
    /// <summary>Asks for a number. Returns false when the user backs out.</summary>
    bool TryReadNumber(string prompt, out double value);

    /// <summary>Asks the user to pick one of a list. Returns -1 when the user backs out.</summary>
    int ReadChoice(string prompt, IReadOnlyList<string> options);
}

/// <summary>
/// One sensitivity analysis operation from the brief. They all run against an already solved model.
/// </summary>
public interface ISensitivityOperation
{
    string Name { get; }

    string Description { get; }

    /// <summary>Returns the text to show on screen and write to the output file.</summary>
    string Execute(SolveResult solved, ISensitivityInput input);
}

/// <summary>
/// Everything the sensitivity operations read off an optimal table, worked out once: B-inverse, the
/// shadow prices, and which columns are basic. Section 11 of the brief is built entirely on these.
/// </summary>
public sealed class SensitivityContext
{
    private SensitivityContext(LinearModel model, CanonicalForm form, Tableau tableau)
    {
        Model = model;
        Form = form;
        Tableau = tableau;
        Inverse = BasisMatrix.CurrentInverse(tableau, form);
        ShadowPrices = BasisMatrix.ShadowPrices(tableau, form);
        ConstraintShadowPrices = BasisMatrix.ConstraintShadowPrices(tableau, form);
        ObjectiveCoefficients = BasisMatrix.ObjectiveCoefficients(form);
    }

    public LinearModel Model { get; }

    public CanonicalForm Form { get; }

    /// <summary>The optimal table every operation works from.</summary>
    public Tableau Tableau { get; }

    public double[,] Inverse { get; }

    /// <summary>CBV * B-inverse in canonical signs (see <see cref="BasisMatrix.ShadowPrices"/>).</summary>
    public double[] ShadowPrices { get; }

    /// <summary>Shadow prices with the sign put back for rows that were negated.</summary>
    public double[] ConstraintShadowPrices { get; }

    /// <summary>The objective coefficient of every tableau column.</summary>
    public double[] ObjectiveCoefficients { get; }

    public ObjectiveSense Sense => Model.Sense;

    public int ConstraintCount => BasisMatrix.ConstraintCount(Tableau);

    /// <summary>Builds the context, or returns null when the result did not come from a tableau method.</summary>
    public static SensitivityContext? From(SolveResult solved)
    {
        if (solved is not { IsOptimal: true, FinalTableau: not null, CanonicalForm: not null })
        {
            return null;
        }

        // Branch & Bound and the Cutting Plane end on a table that has extra bound or cut rows, so
        // B-inverse no longer lines up with the starting basis. Sensitivity analysis is defined on
        // the LP table, so those results are turned away.
        if (BasisMatrix.ConstraintCount(solved.FinalTableau) != solved.CanonicalForm.Rows.Count)
        {
            return null;
        }

        return new SensitivityContext(solved.CanonicalForm.Model, solved.CanonicalForm, solved.FinalTableau);
    }

    /// <summary>Column indices that are currently basic.</summary>
    public IReadOnlyList<int> BasicColumns =>
        Tableau.Basis.Skip(Tableau.FirstConstraintRow).ToArray();

    /// <summary>Column indices that are currently non-basic (the RHS excluded).</summary>
    public IReadOnlyList<int> NonBasicColumns =>
        Enumerable.Range(0, Tableau.RhsColumn).Where(column => !Tableau.Basis.Contains(column)).ToArray();

    public string ColumnName(int column) => Tableau.ColumnNames[column];

    /// <summary>The row a basic column sits in.</summary>
    public int RowOf(int column) => IntegerRules.RowOf(Tableau, column);

    /// <summary>
    /// True when the current z row still passes the optimality test - used to say whether a change
    /// keeps the basis optimal or needs re-optimising.
    /// </summary>
    public bool IsStillOptimal(Tableau tableau)
    {
        for (int column = 0; column < tableau.RhsColumn; column++)
        {
            double value = tableau[Tableau.ObjectiveRow, column];
            bool broken = Sense == ObjectiveSense.Max ? Num.IsNegative(value) : Num.IsPositive(value);
            if (broken)
            {
                return false;
            }
        }

        return !tableau.HasNegativeRightHandSide();
    }

    /// <summary>Re-solves a changed table and writes out every iteration, so a change shows its working.</summary>
    public string ReOptimise(Tableau changed, out Tableau final)
    {
        List<IterationStep> steps = [];
        SimplexRun run = SimplexEngine.Solve(changed, Sense, steps, changed.Label);
        final = run.Tableau;

        var text = new StringWriter();

        if (steps.Count == 0)
        {
            text.WriteLine("  The change does not break optimality, so no further pivoting is needed.");
        }

        foreach (IterationStep step in steps)
        {
            TableauFormatter.Write(text, step);
        }

        TableauFormatter.Write(text, new IterationStep(
            run.Tableau,
            $"{run.Tableau.Label} ({(run.IsOptimal ? "NEW OPTIMAL" : run.Outcome.ToString().ToUpperInvariant())})",
            run.Message));

        if (run.IsOptimal)
        {
            IReadOnlyDictionary<string, double> solution = Form.ReadSolution(run.Tableau);
            text.WriteLine("  New solution:");
            text.WriteLine($"    z = {Num.Format(run.Tableau.ObjectiveValue)}");
            foreach (Variable variable in Model.Variables)
            {
                text.WriteLine($"    {variable.Name} = {Num.Format(solution[variable.Name])}");
            }

        ///Also display the new decision variables that where added when using "Add a new activity".
        HashSet<string> originalVariablesNames = 
        Model.Variables.Select(variables => variables.Name).ToHashSet();

        for (int column = 0; column < run.Tableau.RhsColumn; column++)
        {
            string columnName = run.Tableau.ColumnNames[column];
            if (!columnName.StartsWith("x", StringComparison.OrdinalIgnoreCase) || 
            originalVariablesNames.Contains(columnName))
            {
                continue;
            }
            int row = IntegerRules.RowOf(run.Tableau,column);
            double value = row >= Tableau.FirstConstraintRow 
            ? run.Tableau.Rhs(row) 
            : 0.0;

                text.WriteLine($"    {columnName} = {Num.Format(value)}");
            }
        }

        return text.ToString();
    }

    /// <summary>A copy of the optimal table that an operation can change without disturbing the original.</summary>
    public Tableau CopyTable(string label) => Tableau.Clone(label);

    /// <summary>Writes an allowable range as text, handling the unbounded ends.</summary>
    public static string FormatRange(double lower, double upper) =>
        $"[{(double.IsNegativeInfinity(lower) ? "-infinity" : Num.Format(lower))}, " +
        $"{(double.IsPositiveInfinity(upper) ? "+infinity" : Num.Format(upper))}]";

    /// <summary>Describes a constraint row for a menu prompt.</summary>
    public string DescribeRow(int rowIndex)
    {
        CanonicalRow row = Form.Rows[rowIndex];
        string source = row.SourceConstraintIndex >= 0
            ? Model.Constraints[row.SourceConstraintIndex].Name
            : "generated bound";

        return $"row {row.Label} ({source}, {row.OriginalRelation switch
        {
            Relation.LessOrEqual => "<=",
            Relation.GreaterOrEqual => ">=",
            _ => "="
        }} {Num.Format(OriginalRhs(rowIndex))})";
    }

    /// <summary>The right-hand-side of a row as the user wrote it (before any negation).</summary>
    public double OriginalRhs(int rowIndex)
    {
        double rhs = Form.InitialTableau.Rhs(rowIndex + Tableau.FirstConstraintRow);
        return Form.Rows[rowIndex].Negated ? -rhs : rhs;
    }

    /// <summary>Expands coefficients written per decision variable across the tableau columns.</summary>
    public double[] ExpandDecisionCoefficients(IReadOnlyList<double> perVariable)
    {
        double[] expanded = new double[Tableau.RhsColumn];

        for (int i = 0; i < Model.Variables.Count; i++)
        {
            foreach (SubstitutionTerm term in Form.Substitutions[i].Terms)
            {
                expanded[term.Column] = term.Multiplier * perVariable[i];
            }
        }

        return expanded;
    }

    /// <summary>A heading block naming the table the analysis is being done on.</summary>
    public string Header(string title)
    {
        var text = new StringBuilder();
        text.AppendLine(title);
        text.AppendLine(new string('-', title.Length));
        text.AppendLine();

        var table = new StringWriter();
        TableauFormatter.Write(table, new IterationStep(Tableau, $"{Tableau.Label} (optimal table)", string.Empty));
        text.Append(table);

        return text.ToString();
    }
}
