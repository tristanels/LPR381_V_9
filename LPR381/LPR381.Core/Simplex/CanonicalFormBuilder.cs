using LPR381.Core.Models;

namespace LPR381.Core.Simplex;

/// <summary>
/// Turns the model that was read from the input file into the starting simplex table, following the
/// method in the study notes (Section 1 Step 1, and the constraint set-up table in Section 3):
/// <list type="bullet">
/// <item>The objective moves to the left: max z = 3x1 + 2x2 becomes z - 3x1 - 2x2 = 0.</item>
/// <item>&lt;= adds a slack +s.</item>
/// <item>&gt;= subtracts an excess -e, then the whole row is multiplied by -1.</item>
/// <item>= is split into two rows, one &lt;= and one &gt;= (labelled 2' and 2").</item>
/// </list>
/// No artificial variables and no Big-M: a row that comes out with a negative RHS is handled by the
/// Dual Simplex, exactly as taught.
/// </summary>
public static class CanonicalFormBuilder
{
    /// <summary>One row of the canonical form, still expressed over the original decision variables.</summary>
    private sealed record WorkingRow(
        string Label,
        double[] Coefficients,
        Relation Relation,
        double Rhs,
        int SourceConstraintIndex,
        Relation OriginalRelation);

    public static CanonicalForm Build(LinearModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<string> steps = [];
        List<string> columnNames = [];
        List<double> objectiveCoefficients = [];

        // Column of the tableau for each term of each decision variable, after sign substitution.
        List<VariableSubstitution> substitutions = BuildColumns(model, columnNames, objectiveCoefficients, steps);
        int decisionColumnCount = columnNames.Count;

        List<WorkingRow> rows = BuildRows(model, steps);

        // Slack and excess columns are numbered separately, the way the notes label them.
        int slackCount = 0;
        int excessCount = 0;
        List<(int Row, string Name, double Coefficient)> logicalColumns = [];

        for (int i = 0; i < rows.Count; i++)
        {
            WorkingRow row = rows[i];
            if (row.Relation == Relation.LessOrEqual)
            {
                slackCount++;
                logicalColumns.Add((i, $"s{slackCount}", 1.0));
                steps.Add($"Row {row.Label} (<=): add slack s{slackCount}.");
            }
            else
            {
                excessCount++;
                logicalColumns.Add((i, $"e{excessCount}", -1.0));
                steps.Add(
                    $"Row {row.Label} (>=): subtract excess e{excessCount}, then multiply the row by -1 " +
                    "so the excess column becomes basic (this is what gives the negative RHS).");
            }
        }

        foreach ((int _, string name, double _) in logicalColumns)
        {
            columnNames.Add(name);
        }

        columnNames.Add("rhs");

        int columnCount = columnNames.Count;
        double[,] values = new double[rows.Count + 1, columnCount];
        int[] basis = new int[rows.Count + 1];
        basis[Tableau.ObjectiveRow] = -1;

        // z row: z - c1x1 - c2x2 ... = 0. The same shape is used for max and min; only the
        // optimality test changes (notes, Section 1 Step 3).
        for (int column = 0; column < decisionColumnCount; column++)
        {
            values[Tableau.ObjectiveRow, column] = -objectiveCoefficients[column];
        }

        for (int i = 0; i < rows.Count; i++)
        {
            WorkingRow row = rows[i];
            int tableauRow = i + Tableau.FirstConstraintRow;

            // A >= row is negated in full, which is the step that puts it in canonical form.
            double sign = row.Relation == Relation.GreaterOrEqual ? -1.0 : 1.0;

            for (int column = 0; column < decisionColumnCount; column++)
            {
                values[tableauRow, column] = sign * row.Coefficients[column];
            }

            (int _, string name, double coefficient) = logicalColumns[i];
            int logicalColumn = columnNames.IndexOf(name);
            values[tableauRow, logicalColumn] = sign * coefficient;
            values[tableauRow, columnCount - 1] = sign * row.Rhs;

            // The slack (or the negated excess) starts off basic in its own row.
            basis[tableauRow] = logicalColumn;
        }

        List<string> rowLabels = ["z"];
        rowLabels.AddRange(rows.Select(row => row.Label));

        List<CanonicalRow> canonicalRows = [];
        for (int i = 0; i < rows.Count; i++)
        {
            canonicalRows.Add(new CanonicalRow(
                rows[i].Label,
                rows[i].SourceConstraintIndex,
                rows[i].OriginalRelation,
                rows[i].Relation == Relation.GreaterOrEqual,
                basis[i + Tableau.FirstConstraintRow]));
        }

        var tableau = new Tableau("T-0", columnNames, rowLabels, values, basis);
        return new CanonicalForm(model, tableau, substitutions, steps, canonicalRows, decisionColumnCount);
    }

    /// <summary>
    /// Creates the decision variable columns, applying the sign restrictions from the input file.
    /// </summary>
    private static List<VariableSubstitution> BuildColumns(
        LinearModel model,
        List<string> columnNames,
        List<double> objectiveCoefficients,
        List<string> steps)
    {
        List<VariableSubstitution> substitutions = [];

        foreach (Variable variable in model.Variables)
        {
            switch (variable.SignRestriction)
            {
                case SignRestriction.Negative:
                    // x <= 0, so substitute x = -x' with x' >= 0.
                    steps.Add($"{variable.Name} is restricted to <= 0: substitute {variable.Name} = -{variable.Name}'.");
                    columnNames.Add(variable.Name + "'");
                    objectiveCoefficients.Add(-variable.ObjectiveCoefficient);
                    substitutions.Add(new VariableSubstitution(
                        variable.Name,
                        [new SubstitutionTerm(columnNames.Count - 1, -1.0)]));
                    break;

                case SignRestriction.Unrestricted:
                    // urs, so substitute x = x+ - x- with both parts >= 0.
                    steps.Add(
                        $"{variable.Name} is unrestricted in sign: substitute " +
                        $"{variable.Name} = {variable.Name}+ - {variable.Name}-.");
                    columnNames.Add(variable.Name + "+");
                    objectiveCoefficients.Add(variable.ObjectiveCoefficient);
                    columnNames.Add(variable.Name + "-");
                    objectiveCoefficients.Add(-variable.ObjectiveCoefficient);
                    substitutions.Add(new VariableSubstitution(
                        variable.Name,
                        [
                            new SubstitutionTerm(columnNames.Count - 2, 1.0),
                            new SubstitutionTerm(columnNames.Count - 1, -1.0)
                        ]));
                    break;

                default:
                    if (variable.SignRestriction == SignRestriction.Integer)
                    {
                        steps.Add(
                            $"{variable.Name} is int: solved as a relaxed LP here, the integer restriction " +
                            "is enforced by Branch & Bound or the Cutting Plane algorithm.");
                    }
                    else if (variable.SignRestriction == SignRestriction.Binary)
                    {
                        steps.Add(
                            $"{variable.Name} is bin: relaxed to 0 <= {variable.Name} <= 1 for the LP, the " +
                            "0/1 restriction is enforced by Branch & Bound.");
                    }

                    columnNames.Add(variable.Name);
                    objectiveCoefficients.Add(variable.ObjectiveCoefficient);
                    substitutions.Add(new VariableSubstitution(
                        variable.Name,
                        [new SubstitutionTerm(columnNames.Count - 1, 1.0)]));
                    break;
            }
        }

        return substitutions;
    }

    /// <summary>
    /// Expands the model constraints into canonical rows: equalities are split in two, and every
    /// binary variable gains its upper bound row for the relaxation.
    /// </summary>
    private static List<WorkingRow> BuildRows(LinearModel model, List<string> steps)
    {
        List<WorkingRow> rows = [];

        for (int i = 0; i < model.Constraints.Count; i++)
        {
            Constraint constraint = model.Constraints[i];
            double[] coefficients = ExpandCoefficients(model, constraint.Coefficients);
            string label = (i + 1).ToString();

            if (constraint.Relation == Relation.Equal)
            {
                // An equality is two inequalities: one <= and one >=, labelled 2' and 2".
                steps.Add($"Constraint {constraint.Name} (=): split into rows {label}' (<=) and {label}\" (>=).");
                rows.Add(new WorkingRow(
                    label + "'", coefficients, Relation.LessOrEqual, constraint.RightHandSide, i, Relation.Equal));
                rows.Add(new WorkingRow(
                    label + "\"", (double[])coefficients.Clone(), Relation.GreaterOrEqual, constraint.RightHandSide, i, Relation.Equal));
            }
            else
            {
                rows.Add(new WorkingRow(
                    label, coefficients, constraint.Relation, constraint.RightHandSide, i, constraint.Relation));
            }
        }

        // Binary variables need their x <= 1 bound in the relaxed LP; x >= 0 is already implied.
        int columnCount = CountColumns(model);
        for (int i = 0; i < model.Variables.Count; i++)
        {
            Variable variable = model.Variables[i];
            if (variable.SignRestriction != SignRestriction.Binary)
            {
                continue;
            }

            double[] coefficients = new double[columnCount];
            coefficients[ColumnOf(model, i)] = 1.0;
            rows.Add(new WorkingRow($"b{i + 1}", coefficients, Relation.LessOrEqual, 1.0, -1, Relation.LessOrEqual));
        }

        return rows;
    }

    /// <summary>Spreads coefficients written against the model variables across the tableau columns.</summary>
    private static double[] ExpandCoefficients(LinearModel model, IReadOnlyList<double> coefficients)
    {
        double[] expanded = new double[CountColumns(model)];
        int column = 0;

        for (int i = 0; i < model.Variables.Count; i++)
        {
            switch (model.Variables[i].SignRestriction)
            {
                case SignRestriction.Negative:
                    expanded[column++] = -coefficients[i];
                    break;

                case SignRestriction.Unrestricted:
                    expanded[column++] = coefficients[i];
                    expanded[column++] = -coefficients[i];
                    break;

                default:
                    expanded[column++] = coefficients[i];
                    break;
            }
        }

        return expanded;
    }

    /// <summary>Number of tableau columns the decision variables occupy (urs variables take two).</summary>
    private static int CountColumns(LinearModel model) =>
        model.Variables.Sum(v => v.SignRestriction == SignRestriction.Unrestricted ? 2 : 1);

    /// <summary>The first tableau column belonging to model variable <paramref name="variableIndex"/>.</summary>
    private static int ColumnOf(LinearModel model, int variableIndex)
    {
        int column = 0;
        for (int i = 0; i < variableIndex; i++)
        {
            column += model.Variables[i].SignRestriction == SignRestriction.Unrestricted ? 2 : 1;
        }

        return column;
    }
}
