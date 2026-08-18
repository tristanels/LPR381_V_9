using System.Text;
using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Simplex;

namespace LPR381.Core.Sensitivity;

/// <summary>
/// Brief: "Apply Duality to the programming model. Solve the Dual Programming Model. Verify whether
/// the Programming Model has Strong or Weak Duality."
/// <para>
/// The dual is built by transposing the model: every constraint becomes a variable and every variable
/// becomes a constraint. A max primal with &lt;= rows gives a min dual with &gt;= rows, and the other way
/// round. Constraints are normalised into the right direction first, and an equality becomes two rows.
/// </para>
/// <para>
/// The dual is then solved with the Primal Simplex, and the two objective values are compared:
/// equal means strong duality, a gap means only weak duality holds.
/// </para>
/// </summary>
public sealed class DualityOperation : SensitivityOperation
{
    public override string Name => "Duality";

    public override string Description =>
        "Build the dual model, solve it, and check for strong or weak duality.";

    protected override string Run(SensitivityContext context, ISensitivityInput input)
    {
        (LinearModel dual, List<string> notes) = BuildDual(context.Model);

        var text = new StringBuilder();
        text.Append(context.Header("Duality"));

        text.AppendLine("  PRIMAL MODEL");
        text.AppendLine();
        text.AppendLine(Indent(context.Model.Describe()));
        text.AppendLine();

        text.AppendLine("  How the dual was built:");
        foreach (string note in notes)
        {
            text.AppendLine("    - " + note);
        }

        text.AppendLine();
        text.AppendLine("  DUAL MODEL");
        text.AppendLine();
        text.AppendLine(Indent(dual.Describe()));
        text.AppendLine();

        SolveResult dualResult = new PrimalSimplexSolver().Solve(dual);

        var report = new StringWriter();
        ReportWriter.Write(report, dual, dualResult);
        text.AppendLine("  SOLVING THE DUAL");
        text.AppendLine();
        text.AppendLine(report.ToString());

        if (!dualResult.IsOptimal)
        {
            text.AppendLine($"  The dual came out {dualResult.Status}, so the duality check cannot be completed.");
            return text.ToString();
        }

        double primalObjective = context.Tableau.ObjectiveValue;
        double dualObjective = dualResult.ObjectiveValue;
        double gap = Math.Abs(primalObjective - dualObjective);

        text.AppendLine("  DUALITY CHECK");
        text.AppendLine();
        text.AppendLine($"    Primal z     = {Num.Format(primalObjective)}");
        text.AppendLine($"    Dual   w     = {Num.Format(dualObjective)}");
        text.AppendLine($"    Duality gap  = {Num.Format(gap)}");
        text.AppendLine();

        text.AppendLine(Num.IsZero(gap)
            ? "    z = w, so the duality gap is zero and the model has STRONG DUALITY."
            : "    z is not equal to w, so a duality gap remains and only WEAK DUALITY holds. " +
              (context.Model.Sense == ObjectiveSense.Max
                  ? "For a max primal, weak duality says z <= w."
                  : "For a min primal, weak duality says z >= w."));

        text.AppendLine();
        text.AppendLine("  SHADOW PRICE RELATIONSHIP");
        text.AppendLine();
        text.AppendLine("    The optimal dual variables are the primal shadow prices - the same numbers seen twice.");
        text.AppendLine();

        List<IReadOnlyList<string>> rows = [];
        for (int i = 0; i < dual.Variables.Count && i < context.ConstraintCount; i++)
        {
            rows.Add(
            [
                context.Form.Rows[i].Label,
                Num.Format(context.ConstraintShadowPrices[i]),
                dual.Variables[i].Name,
                Num.Format(dualResult.VariableValues![dual.Variables[i].Name])
            ]);
        }

        text.Append(MatrixFormatter.Table(["primal row", "shadow price", "dual variable", "value"], rows));

        if (context.Model.IsIntegerModel)
        {
            text.AppendLine();
            text.AppendLine("    Note: this model has int/bin restrictions. Duality is defined on the LP, so the " +
                            "relaxed model is what was dualised.");
        }

        return text.ToString();
    }

    /// <summary>
    /// Transposes the model. Constraints are first normalised into the direction that matches the
    /// objective (&lt;= for a max, &gt;= for a min), because that is the form the dual is defined on.
    /// </summary>
    public static (LinearModel Dual, List<string> Notes) BuildDual(LinearModel primal)
    {
        ArgumentNullException.ThrowIfNull(primal);

        bool primalIsMax = primal.Sense == ObjectiveSense.Max;
        Relation wanted = primalIsMax ? Relation.LessOrEqual : Relation.GreaterOrEqual;

        List<string> notes =
        [
            primalIsMax
                ? "The primal is a max, so every constraint is put into <= form and the dual is a min."
                : "The primal is a min, so every constraint is put into >= form and the dual is a max."
        ];

        // Normalise: flip the rows pointing the wrong way, split the equalities.
        List<(double[] Coefficients, double Rhs, string Source)> rows = [];

        foreach (Constraint constraint in primal.Constraints)
        {
            double[] coefficients = constraint.Coefficients.ToArray();

            if (constraint.Relation == Relation.Equal)
            {
                notes.Add($"{constraint.Name} is an equality, so it becomes two rows (one each way), " +
                          "which gives it two dual variables.");
                rows.Add((coefficients, constraint.RightHandSide, constraint.Name + "a"));
                rows.Add((Negate(coefficients), -constraint.RightHandSide, constraint.Name + "b"));
            }
            else if (constraint.Relation != wanted)
            {
                notes.Add($"{constraint.Name} points the wrong way, so the row is multiplied by -1.");
                rows.Add((Negate(coefficients), -constraint.RightHandSide, constraint.Name));
            }
            else
            {
                rows.Add((coefficients, constraint.RightHandSide, constraint.Name));
            }
        }

        notes.Add($"Each of the {rows.Count} row(s) becomes a dual variable, and each of the " +
                  $"{primal.Variables.Count} decision variable(s) becomes a dual constraint.");

        // The dual objective takes the primal right-hand-sides as its coefficients.
        List<Variable> dualVariables = [];
        for (int i = 0; i < rows.Count; i++)
        {
            dualVariables.Add(new Variable($"y{i + 1}", rows[i].Rhs, SignRestriction.Positive));
        }

        // Each primal variable gives one dual constraint, built from that variable's column.
        List<Constraint> dualConstraints = [];
        Relation dualRelation = primalIsMax ? Relation.GreaterOrEqual : Relation.LessOrEqual;

        for (int j = 0; j < primal.Variables.Count; j++)
        {
            double[] column = rows.Select(row => row.Coefficients[j]).ToArray();
            dualConstraints.Add(new Constraint(
                $"d{j + 1}", column, dualRelation, primal.Variables[j].ObjectiveCoefficient));
        }

        var dual = new LinearModel(
            primalIsMax ? ObjectiveSense.Min : ObjectiveSense.Max,
            dualVariables,
            dualConstraints);

        return (dual, notes);
    }

    private static double[] Negate(IReadOnlyList<double> values) => values.Select(value => -value).ToArray();

    private static string Indent(string text) =>
        string.Join(Environment.NewLine, text.Split(Environment.NewLine).Select(line => "    " + line));
}
