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
/// becomes a constraint. The sign of each dual variable follows the primal constraint relation, while
/// the relation of each dual constraint follows the primal variable sign restriction. Binary variables
/// add their LP-relaxation upper-bound rows before dualising.
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
        text.AppendLine("    For ordinary LP rows, the optimal dual variables correspond to the primal shadow prices.");
        text.AppendLine();

        List<IReadOnlyList<string>> rows = [];
        for (int i = 0; i < context.Model.Constraints.Count && i < dual.Variables.Count; i++)
        {
            Constraint original = context.Model.Constraints[i];

            int[] matchingRows = context.Form.Rows
                .Select((row, index) => (row, index))
                .Where(item => item.row.SourceConstraintIndex == i)
                .Select(item => item.index)
                .ToArray();

            string shadowPrice = original.Relation == Relation.Equal
                ? "see split equality rows"
                : matchingRows.Length == 1
                    ? Num.Format(context.ConstraintShadowPrices[matchingRows[0]])
                    : "n/a";

            rows.Add(
            [
                original.Name,
                shadowPrice,
                dual.Variables[i].Name,
                Num.Format(dualResult.VariableValues![dual.Variables[i].Name])
            ]);
        }

        text.Append(MatrixFormatter.Table(["primal constraint", "shadow price", "dual variable", "value"], rows));

        if (context.Model.IsIntegerModel)
        {
            text.AppendLine();
            text.AppendLine("    Note: this model has int/bin restrictions. Duality is defined on the LP, so the " +
                            "relaxed model is what was dualised.");
        }

        return text.ToString();
    }

    /// <summary>
    /// Transposes the model using the standard primal/dual sign rules. The input model is copied so
    /// the caller's model is never changed. Integer restrictions are relaxed and binary variables add
    /// their x <= 1 relaxation rows.
    /// </summary>
    public static (LinearModel Dual, List<string> Notes) BuildDual(LinearModel primal)
    {
        ArgumentNullException.ThrowIfNull(primal);

        // Duality is applied to the LP relaxation. Integer restrictions do not change the sign of a
        // variable, and a binary variable contributes its usual x >= 0 restriction plus the explicit
        // upper-bound row x <= 1 used by the relaxation in CanonicalFormBuilder.
        List<Constraint> primalConstraints = primal.Constraints
            .Select(c => new Constraint(
                c.Name,
                c.Coefficients.ToArray(),
                c.Relation,
                c.RightHandSide))
            .ToList();

        List<string> notes =
        [
            primal.Sense == ObjectiveSense.Max
                ? "The primal is a max, so its dual is a min."
                : "The primal is a min, so its dual is a max."
        ];

        for (int i = 0; i < primal.Variables.Count; i++)
        {
            Variable variable = primal.Variables[i];

            if (variable.SignRestriction == SignRestriction.Binary)
            {
                double[] coefficients = new double[primal.Variables.Count];
                coefficients[i] = 1.0;

                primalConstraints.Add(new Constraint(
                    $"b{i + 1}",
                    coefficients,
                    Relation.LessOrEqual,
                    1.0));

                notes.Add(
                    $"{variable.Name} is binary, so the LP relaxation includes the bound " +
                    $"{variable.Name} <= 1 before dualising.");
            }
            else if (variable.SignRestriction == SignRestriction.Integer)
            {
                notes.Add(
                    $"{variable.Name} is integer, so its integrality is relaxed for the dual LP.");
            }
        }

        // Standard dual signs:
        //   max primal: <= row -> y >= 0, >= row -> y <= 0, = row -> y unrestricted.
        //   min primal: >= row -> y >= 0, <= row -> y <= 0, = row -> y unrestricted.
        List<Variable> dualVariables = [];
        for (int i = 0; i < primalConstraints.Count; i++)
        {
            Constraint constraint = primalConstraints[i];
            SignRestriction sign = DualVariableSign(primal.Sense, constraint.Relation);

            dualVariables.Add(
                new Variable($"y{i + 1}", constraint.RightHandSide, sign));

            notes.Add(
                $"{constraint.Name} ({constraint.RelationSymbol}) becomes dual variable y{i + 1} " +
                $"with sign restriction {DescribeSign(sign)}.");
        }

        // Standard dual constraint signs depend on the sign restriction of the corresponding primal
        // variable. Integer and binary variables are non-negative in the LP relaxation.
        List<Constraint> dualConstraints = [];
        for (int j = 0; j < primal.Variables.Count; j++)
        {
            Variable primalVariable = primal.Variables[j];

            double[] column = primalConstraints
                .Select(constraint => constraint.Coefficients[j])
                .ToArray();

            Relation relation = DualConstraintRelation(primal.Sense, primalVariable.SignRestriction);

            dualConstraints.Add(new Constraint(
                $"d{j + 1}",
                column,
                relation,
                primalVariable.ObjectiveCoefficient));

            notes.Add(
                $"{primalVariable.Name} ({DescribeSign(primalVariable.SignRestriction)}) gives dual " +
                $"constraint d{j + 1} with relation {RelationSymbol(relation)}.");
        }

        var dual = new LinearModel(
            primal.Sense == ObjectiveSense.Max ? ObjectiveSense.Min : ObjectiveSense.Max,
            dualVariables,
            dualConstraints);

        return (dual, notes);
    }

    private static SignRestriction DualVariableSign(ObjectiveSense primalSense, Relation relation)
    {
        if (relation == Relation.Equal)
        {
            return SignRestriction.Unrestricted;
        }

        if (primalSense == ObjectiveSense.Max)
        {
            return relation == Relation.LessOrEqual
                ? SignRestriction.Positive
                : SignRestriction.Negative;
        }

        return relation == Relation.GreaterOrEqual
            ? SignRestriction.Positive
            : SignRestriction.Negative;
    }

    private static Relation DualConstraintRelation(ObjectiveSense primalSense, SignRestriction restriction)
    {
        bool nonNegative = restriction is SignRestriction.Positive or SignRestriction.Integer or SignRestriction.Binary;

        if (restriction == SignRestriction.Unrestricted)
        {
            return Relation.Equal;
        }

        if (primalSense == ObjectiveSense.Max)
        {
            return nonNegative ? Relation.GreaterOrEqual : Relation.LessOrEqual;
        }

        return nonNegative ? Relation.LessOrEqual : Relation.GreaterOrEqual;
    }

    private static string DescribeSign(SignRestriction sign) => sign switch
    {
        SignRestriction.Positive => ">= 0",
        SignRestriction.Negative => "<= 0",
        SignRestriction.Unrestricted => "unrestricted",
        SignRestriction.Integer => ">= 0 and integer",
        SignRestriction.Binary => "0 or 1",
        _ => sign.ToString()
    };

    private static string RelationSymbol(Relation relation) => relation switch
    {
        Relation.LessOrEqual => "<=",
        Relation.GreaterOrEqual => ">=",
        Relation.Equal => "=",
        _ => "?"
    };

    private static double[] Negate(IReadOnlyList<double> values) => values.Select(value => -value).ToArray();

    private static string Indent(string text) =>
        string.Join(Environment.NewLine, text.Split(Environment.NewLine).Select(line => "    " + line));
}
