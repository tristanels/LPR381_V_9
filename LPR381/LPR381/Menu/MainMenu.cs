using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Sensitivity;
using LPR381.Core.Simplex;

namespace LPR381.App.Menu;

/// <summary>
/// The menu driven front end required by the brief. It holds the model that is currently loaded and
/// the last result that was produced, and hands the work off to LPR381.Core.
/// </summary>
public sealed class MainMenu
{
    private LinearModel? _model;
    private string? _modelPath;
    private SolveResult? _lastResult;

    public void Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        while (true)
        {
            ConsoleHelper.WriteHeading("LPR381 - Linear and Integer Programming Solver");
            Console.WriteLine();
            Console.WriteLine($"  Model  : {_modelPath ?? "(none loaded)"}");
            Console.WriteLine($"  Result : {(_lastResult is null ? "(not solved yet)" : _lastResult.Status.ToString())}");
            Console.WriteLine();
            Console.WriteLine("  1. Load a programming model from an input file");
            Console.WriteLine("  2. View the model and its canonical form");
            Console.WriteLine("  3. Solve the model (choose an algorithm)");
            Console.WriteLine("  4. Sensitivity analysis");
            Console.WriteLine("  5. Export the results to an output file");
            Console.WriteLine("  6. Input file format help");
            Console.WriteLine("  0. Exit");
            Console.WriteLine();

            string choice = ConsoleHelper.Prompt("  Select an option: ");

            try
            {
                switch (choice)
                {
                    case "1": LoadModel(); break;
                    case "2": ViewModel(); break;
                    case "3": SolveModel(); break;
                    case "4": SensitivityAnalysis(); break;
                    case "5": ExportResults(); break;
                    case "6": ShowFormatHelp(); break;
                    case "0": return;
                    default:
                        ConsoleHelper.WriteError("That is not one of the options. Enter a number from 0 to 6.");
                        ConsoleHelper.Pause();
                        break;
                }
            }
            catch (ModelParseException error)
            {
                // Bad input file: show the line and what was expected, never a stack trace.
                ConsoleHelper.WriteError("The input file could not be read.");
                ConsoleHelper.WriteError(error.Message);
                ConsoleHelper.Pause();
            }
            catch (Exception error)
            {
                ConsoleHelper.WriteError("Something went wrong: " + error.Message);
                ConsoleHelper.Pause();
            }
        }
    }

    private void LoadModel()
    {
        ConsoleHelper.WriteHeading("Load a programming model");
        Console.WriteLine();

        string[] samples = FindSampleFiles();
        if (samples.Length > 0)
        {
            Console.WriteLine("  Sample files available:");
            for (int i = 0; i < samples.Length; i++)
            {
                Console.WriteLine($"    {i + 1}. {Path.GetFileName(samples[i])}");
            }

            Console.WriteLine();
        }

        string answer = ConsoleHelper.Prompt("  File path (or the number of a sample): ");
        if (answer.Length == 0)
        {
            return;
        }

        string path = answer;
        if (int.TryParse(answer, out int index) && index >= 1 && index <= samples.Length)
        {
            path = samples[index - 1];
        }

        _model = ModelFileParser.ParseFile(path);
        _modelPath = Path.GetFullPath(path);
        _lastResult = null;

        ConsoleHelper.WriteSuccess($"Loaded {_model.Variables.Count} decision variable(s) and {_model.Constraints.Count} constraint(s).");
        Console.WriteLine();
        Console.WriteLine(_model.Describe());
        ConsoleHelper.Pause();
    }

    private void ViewModel()
    {
        if (!RequireModel())
        {
            return;
        }

        ConsoleHelper.WriteHeading("Model and canonical form");
        Console.WriteLine();
        Console.WriteLine("PROGRAMMING MODEL");
        Console.WriteLine();
        Console.WriteLine(_model!.Describe());
        Console.WriteLine();

        CanonicalForm canonicalForm = CanonicalFormBuilder.Build(_model);
        Console.WriteLine(canonicalForm.Describe());

        var step = new IterationStep(canonicalForm.InitialTableau, "T-0 (starting table)", string.Empty);
        TableauFormatter.Write(Console.Out, step);

        if (canonicalForm.RequiresDualSimplex)
        {
            ConsoleHelper.WriteWarning(
                "This table has a negative RHS, so the Dual Simplex runs first and the Primal Simplex finishes it.");
        }

        ConsoleHelper.Pause();
    }

    private void SolveModel()
    {
        if (!RequireModel())
        {
            return;
        }

        ConsoleHelper.WriteHeading("Choose an algorithm");
        Console.WriteLine();

        IReadOnlyList<ISolver> solvers = SolverRegistry.All;
        for (int i = 0; i < solvers.Count; i++)
        {
            ISolver solver = solvers[i];
            string tag = solver.IsImplemented ? string.Empty : "   [not implemented yet]";
            Console.WriteLine($"  {i + 1}. {solver.Name}{tag}");
            Console.WriteLine($"     {solver.Description}");
        }

        Console.WriteLine("  0. Back");
        Console.WriteLine();

        string answer = ConsoleHelper.Prompt("  Select an algorithm: ");
        if (!int.TryParse(answer, out int choice) || choice < 1 || choice > solvers.Count)
        {
            return;
        }

        ISolver chosen = solvers[choice - 1];

        // CanSolve explains up front why an algorithm does not suit this model, instead of
        // letting it produce a wrong answer.
        if (!chosen.CanSolve(_model!, out string reason))
        {
            ConsoleHelper.WriteWarning(reason);

            if (!chosen.IsImplemented)
            {
                SolveResult placeholder = chosen.Solve(_model!);
                ConsoleHelper.WriteWarning(placeholder.Message);
            }

            ConsoleHelper.Pause();
            return;
        }

        if (reason.Length > 0)
        {
            ConsoleHelper.WriteWarning(reason);
        }

        SolveResult result = chosen.Solve(_model!);
        _lastResult = result;

        ConsoleHelper.WriteHeading($"{chosen.Name} - working");
        Console.WriteLine();
        ReportWriter.Write(Console.Out, _model!, result);

        if (result.IsOptimal)
        {
            ConsoleHelper.WriteSuccess("Solved. Use option 5 to export this working to an output file.");
        }
        else
        {
            ConsoleHelper.WriteWarning(result.Message);
        }

        ConsoleHelper.Pause();
    }

    private void SensitivityAnalysis()
    {
        if (!RequireModel())
        {
            return;
        }

        if (_lastResult is null || !_lastResult.IsOptimal)
        {
            ConsoleHelper.WriteError("Solve the model to optimality first (option 3), then come back here.");
            ConsoleHelper.Pause();
            return;
        }

        ConsoleHelper.WriteHeading("Sensitivity analysis");
        Console.WriteLine();

        IReadOnlyList<ISensitivityOperation> operations = SensitivityRegistry.All;
        for (int i = 0; i < operations.Count; i++)
        {
            string tag = operations[i].IsImplemented ? string.Empty : "   [not implemented yet]";
            Console.WriteLine($"  {i + 1,2}. {operations[i].Name}{tag}");
        }

        Console.WriteLine("   0. Back");
        Console.WriteLine();

        string answer = ConsoleHelper.Prompt("  Select an operation: ");
        if (!int.TryParse(answer, out int choice) || choice < 1 || choice > operations.Count)
        {
            return;
        }

        ISensitivityOperation operation = operations[choice - 1];
        Console.WriteLine();
        Console.WriteLine($"  {operation.Name}: {operation.Description}");
        Console.WriteLine();
        Console.WriteLine("  " + operation.Execute(_lastResult).Replace(Environment.NewLine, Environment.NewLine + "  "));
        ConsoleHelper.Pause();
    }

    private void ExportResults()
    {
        if (_model is null || _lastResult is null)
        {
            ConsoleHelper.WriteError("There is nothing to export yet. Load a model and solve it first.");
            ConsoleHelper.Pause();
            return;
        }

        string defaultPath = Path.Combine(Environment.CurrentDirectory, "output.txt");
        string answer = ConsoleHelper.Prompt($"  Output file [{defaultPath}]: ");
        string path = answer.Length == 0 ? defaultPath : answer;

        using (var writer = new StreamWriter(path, append: false))
        {
            ReportWriter.Write(writer, _model, _lastResult);
        }

        ConsoleHelper.WriteSuccess($"Results written to {Path.GetFullPath(path)}");
        ConsoleHelper.Pause();
    }

    private static void ShowFormatHelp()
    {
        ConsoleHelper.WriteHeading("Input file format");
        Console.WriteLine();
        Console.WriteLine("  Line 1      : max or min, then a signed coefficient for each decision variable.");
        Console.WriteLine("  Lines 2..n  : a signed technological coefficient for each decision variable,");
        Console.WriteLine("                then the relation (<=, >= or =) and the right-hand-side.");
        Console.WriteLine("  Last line   : one sign restriction per decision variable: + - urs int bin");
        Console.WriteLine();
        Console.WriteLine("  Example (the knapsack from the brief):");
        Console.WriteLine();
        Console.WriteLine("      max +2 +3 +3 +5 +2 +4");
        Console.WriteLine("      +11 +8 +6 +14 +10 +10 <=40");
        Console.WriteLine("      bin bin bin bin bin bin");
        Console.WriteLine();
        Console.WriteLine("  Any number of decision variables and constraints is accepted.");
        ConsoleHelper.Pause();
    }

    private bool RequireModel()
    {
        if (_model is not null)
        {
            return true;
        }

        ConsoleHelper.WriteError("Load an input file first (option 1).");
        ConsoleHelper.Pause();
        return false;
    }

    /// <summary>Looks for the sample models that ship next to solve.exe.</summary>
    private static string[] FindSampleFiles()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "samples");
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.txt").OrderBy(path => path).ToArray()
            : [];
    }
}
