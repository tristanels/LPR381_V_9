using LPR381.Core.Simplex;

namespace LPR381.Core.Algorithms;

/// <summary>How a solve ended.</summary>
public enum SolutionStatus
{
    Optimal,
    Infeasible,
    Unbounded,

    /// <summary>The algorithm is a placeholder that still has to be written.</summary>
    NotImplemented,

    /// <summary>The algorithm cannot be applied to this model, or something else went wrong.</summary>
    Error
}

/// <summary>
/// One step of the working. Most steps carry a simplex table, but an algorithm may instead supply a
/// block of pre-formatted text (the Revised Simplex prints matrices, Knapsack B&amp;B prints its fill
/// table), so <see cref="Tableau"/> is optional and <see cref="Body"/> can carry anything else.
/// The brief requires every iteration to be displayed, so nothing here is thrown away.
/// </summary>
public sealed class IterationStep
{
    public IterationStep(
        Tableau tableau,
        string heading,
        string narrative,
        int pivotRow = -1,
        int pivotColumn = -1,
        double[]? ratios = null,
        RatioAxis ratioAxis = RatioAxis.Rows,
        string ratioHeading = "ratio",
        string body = "")
    {
        Tableau = tableau;
        Heading = heading;
        Narrative = narrative;
        PivotRow = pivotRow;
        PivotColumn = pivotColumn;
        Ratios = ratios;
        RatioAxis = ratioAxis;
        RatioHeading = ratioHeading;
        Body = body;
    }

    private IterationStep(string heading, string body, string narrative)
    {
        Heading = heading;
        Body = body;
        Narrative = narrative;
        PivotRow = -1;
        PivotColumn = -1;
        RatioHeading = "ratio";
    }

    /// <summary>A step made of text only - a matrix, a node summary, a ranking table.</summary>
    public static IterationStep Text(string heading, string body, string narrative = "") =>
        new(heading, body, narrative);

    public Tableau? Tableau { get; }

    /// <summary>Short title for the step, e.g. "T-1 (Primal Simplex)".</summary>
    public string Heading { get; }

    /// <summary>Pre-formatted extra text printed under the heading (and under the table, if any).</summary>
    public string Body { get; } = string.Empty;

    /// <summary>Plain-language explanation of the choice made at this step.</summary>
    public string Narrative { get; }

    /// <summary>-1 when this table was not pivoted (the final, optimal one).</summary>
    public int PivotRow { get; }

    /// <summary>-1 when this table was not pivoted.</summary>
    public int PivotColumn { get; }

    public double[]? Ratios { get; }

    public RatioAxis RatioAxis { get; }

    public string RatioHeading { get; }

    public bool HasPivot => PivotRow >= 0 && PivotColumn >= 0;
}

/// <summary>Everything an algorithm produced: the answer, and all the working that led to it.</summary>
public sealed class SolveResult
{
    private SolveResult(
        string algorithmName,
        SolutionStatus status,
        string message,
        CanonicalForm? canonicalForm,
        IReadOnlyList<IterationStep> steps,
        double objectiveValue,
        IReadOnlyDictionary<string, double>? variableValues,
        Tableau? finalTableau)
    {
        AlgorithmName = algorithmName;
        Status = status;
        Message = message;
        CanonicalForm = canonicalForm;
        Steps = steps;
        ObjectiveValue = objectiveValue;
        VariableValues = variableValues;
        FinalTableau = finalTableau;
    }

    public string AlgorithmName { get; }

    public SolutionStatus Status { get; }

    /// <summary>A sentence the menu can show the user as-is.</summary>
    public string Message { get; }

    public CanonicalForm? CanonicalForm { get; }

    public IReadOnlyList<IterationStep> Steps { get; }

    public double ObjectiveValue { get; }

    /// <summary>Values for the decision variables of the original model, null unless the status is Optimal.</summary>
    public IReadOnlyDictionary<string, double>? VariableValues { get; }

    /// <summary>
    /// The optimal table. Sensitivity analysis works off this, which is why it is kept separately -
    /// the last step of a Branch &amp; Bound run is a summary, not the winning table.
    /// </summary>
    public Tableau? FinalTableau { get; }

    public bool IsOptimal => Status == SolutionStatus.Optimal;

    /// <summary>True when the answer came from a tableau method, so sensitivity analysis can run on it.</summary>
    public bool SupportsSensitivityAnalysis => IsOptimal && FinalTableau is not null && CanonicalForm is not null;

    public static SolveResult Optimal(
        string algorithmName,
        CanonicalForm canonicalForm,
        IReadOnlyList<IterationStep> steps,
        double objectiveValue,
        IReadOnlyDictionary<string, double> variableValues,
        string message = "Optimal solution found.",
        Tableau? finalTableau = null) =>
        new(algorithmName, SolutionStatus.Optimal, message, canonicalForm, steps, objectiveValue, variableValues, finalTableau);

    public static SolveResult Failed(
        string algorithmName,
        SolutionStatus status,
        string message,
        CanonicalForm? canonicalForm = null,
        IReadOnlyList<IterationStep>? steps = null) =>
        new(algorithmName, status, message, canonicalForm, steps ?? [], 0.0, null, null);

    public static SolveResult NotImplemented(string algorithmName, string message) =>
        new(algorithmName, SolutionStatus.NotImplemented, message, null, [], 0.0, null, null);
}
