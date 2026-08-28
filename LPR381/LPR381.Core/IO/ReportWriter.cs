using LPR381.Core.Algorithms;
using LPR381.Core.Models;
using LPR381.Core.NonLinear;

namespace LPR381.Core.IO;

/// <summary>
/// Produces the full report: the model as it was read, the canonical form, every table iteration and
/// the final answer. The brief requires the output file to contain the canonical form and all the
/// tableau iterations of the selected algorithm, with values rounded to three decimals.
/// </summary>
public static class ReportWriter
{
    private const string Divider = "======================================================================";
    private const string ThinDivider = "----------------------------------------------------------------------";

    public static void Write(
        TextWriter writer,
        LinearModel model,
        SolveResult result,
        IReadOnlyList<string>? sensitivityLog = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        Write(writer, model.Describe(), [.. model.Variables.Select(v => v.Name)], "z", result, sensitivityLog);
    }

    /// <summary>
    /// The same report for a non-linear model. It has no canonical form and no tables, so only the
    /// model block, the search steps and the answer come out - the rest of the layout is identical.
    /// </summary>
    public static void Write(
        TextWriter writer,
        NonLinearModel model,
        SolveResult result,
        IReadOnlyList<string>? sensitivityLog = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        Write(writer, model.Describe(), [model.VariableName], $"f({model.VariableName})", result, sensitivityLog);
    }

    private static void Write(
        TextWriter writer,
        string modelDescription,
        IReadOnlyList<string> variableNames,
        string objectiveLabel,
        SolveResult result,
        IReadOnlyList<string>? sensitivityLog)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine(Divider);
        writer.WriteLine("  LPR381 - Linear and Integer Programming Solver");
        writer.WriteLine($"  Algorithm : {result.AlgorithmName}");
        writer.WriteLine($"  Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine(Divider);
        writer.WriteLine();

        writer.WriteLine("PROGRAMMING MODEL (as entered in the input file)");
        writer.WriteLine();
        writer.WriteLine(modelDescription);
        writer.WriteLine();
        writer.WriteLine(ThinDivider);
        writer.WriteLine();

        if (result.CanonicalForm is not null)
        {
            writer.WriteLine(result.CanonicalForm.Describe());
            writer.WriteLine(ThinDivider);
            writer.WriteLine();
        }

        if (result.Steps.Count > 0)
        {
            writer.WriteLine(result.CanonicalForm is null ? "ITERATIONS" : "TABLE ITERATIONS");
            writer.WriteLine();

            foreach (IterationStep step in result.Steps)
            {
                TableauFormatter.Write(writer, step);
            }

            writer.WriteLine(ThinDivider);
            writer.WriteLine();
        }

        WriteResult(writer, variableNames, objectiveLabel, result);

        if (sensitivityLog is { Count: > 0 })
        {
            writer.WriteLine(ThinDivider);
            writer.WriteLine();
            writer.WriteLine("SENSITIVITY ANALYSIS");
            writer.WriteLine();

            foreach (string entry in sensitivityLog)
            {
                writer.WriteLine(entry);
                writer.WriteLine();
            }
        }
    }

    /// <summary>The final answer block: status, z, and a value for every decision variable.</summary>
    public static void WriteResult(TextWriter writer, LinearModel model, SolveResult result)
    {
        ArgumentNullException.ThrowIfNull(model);
        WriteResult(writer, [.. model.Variables.Select(v => v.Name)], "z", result);
    }

    private static void WriteResult(
        TextWriter writer,
        IReadOnlyList<string> variableNames,
        string objectiveLabel,
        SolveResult result)
    {
        writer.WriteLine("RESULT");
        writer.WriteLine();
        writer.WriteLine($"  Status : {DescribeStatus(result.Status)}");
        writer.WriteLine($"  {result.Message}");

        if (!result.IsOptimal || result.VariableValues is null)
        {
            writer.WriteLine();
            return;
        }

        writer.WriteLine();
        writer.WriteLine($"  {objectiveLabel} = {Num.Format(result.ObjectiveValue)}");

        foreach (string name in variableNames)
        {
            writer.WriteLine($"  {name} = {Num.Format(result.VariableValues[name])}");
        }

        writer.WriteLine();
    }

    private static string DescribeStatus(SolutionStatus status) => status switch
    {
        SolutionStatus.Optimal => "OPTIMAL",
        SolutionStatus.Infeasible => "INFEASIBLE",
        SolutionStatus.Unbounded => "UNBOUNDED",
        SolutionStatus.NotImplemented => "NOT IMPLEMENTED",
        _ => "ERROR"
    };
}
