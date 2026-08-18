using LPR381.Core.Algorithms;
using LPR381.Core.Models;

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
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine(Divider);
        writer.WriteLine("  LPR381 - Linear and Integer Programming Solver");
        writer.WriteLine($"  Algorithm : {result.AlgorithmName}");
        writer.WriteLine($"  Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine(Divider);
        writer.WriteLine();

        writer.WriteLine("PROGRAMMING MODEL (as entered in the input file)");
        writer.WriteLine();
        writer.WriteLine(model.Describe());
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
            writer.WriteLine("TABLE ITERATIONS");
            writer.WriteLine();

            foreach (IterationStep step in result.Steps)
            {
                TableauFormatter.Write(writer, step);
            }

            writer.WriteLine(ThinDivider);
            writer.WriteLine();
        }

        WriteResult(writer, model, result);

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
        writer.WriteLine($"  z = {Num.Format(result.ObjectiveValue)}");

        foreach (Variable variable in model.Variables)
        {
            double value = result.VariableValues[variable.Name];
            writer.WriteLine($"  {variable.Name} = {Num.Format(value)}");
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
