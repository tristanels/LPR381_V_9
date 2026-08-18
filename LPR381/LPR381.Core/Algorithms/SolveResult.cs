using LPR381.Core.Simplex;

namespace LPR381.Core.Algorithms;

/// <summary>How a solve ended.</summary>
public enum SolutionStatus
{
    Optimal,
    Infeasible,
    Unbounded,

    /// <summary>The algorithm is a placeholder that a team member still has to fill in.</summary>
    NotImplemented,

    /// <summary>The algorithm cannot be applied to this model, or something else went wrong.</summary>
    Error
}

/// <summary>
/// One step of the working: the table as it stood, which row and column were chosen, the ratios that
/// were worked out, and a sentence saying why. The brief requires every iteration to be displayed,
/// so nothing here is thrown away.
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
        string ratioHeading = "ratio")
    {
        Tableau = tableau;
        Heading = heading;
        Narrative = narrative;
        PivotRow = pivotRow;
        PivotColumn = pivotColumn;
        Ratios = ratios;
        RatioAxis = ratioAxis;
        RatioHeading = ratioHeading;
    }

    public Tableau Tableau { get; }

    /// <summary>Short title for the step, e.g. "T-1 (Primal Simplex)".</summary>
    public string Heading { get; }

    /// <summary>Plain-language explanation of the choice made on this table.</summary>
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
        IReadOnlyDictionary<string, double>? variableValues)
    {
        AlgorithmName = algorithmName;
        Status = status;
        Message = message;
        CanonicalForm = canonicalForm;
        Steps = steps;
        ObjectiveValue = objectiveValue;
        VariableValues = variableValues;
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

    public bool IsOptimal => Status == SolutionStatus.Optimal;

    public static SolveResult Optimal(
        string algorithmName,
        CanonicalForm canonicalForm,
        IReadOnlyList<IterationStep> steps,
        double objectiveValue,
        IReadOnlyDictionary<string, double> variableValues,
        string message = "Optimal solution found.") =>
        new(algorithmName, SolutionStatus.Optimal, message, canonicalForm, steps, objectiveValue, variableValues);

    public static SolveResult Failed(
        string algorithmName,
        SolutionStatus status,
        string message,
        CanonicalForm? canonicalForm = null,
        IReadOnlyList<IterationStep>? steps = null) =>
        new(algorithmName, status, message, canonicalForm, steps ?? [], 0.0, null);

    public static SolveResult NotImplemented(string algorithmName, string message) =>
        new(algorithmName, SolutionStatus.NotImplemented, message, null, [], 0.0, null);
}
