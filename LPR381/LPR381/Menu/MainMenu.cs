using LPR381.Core.Algorithms;
using LPR381.Core.IO;
using LPR381.Core.Models;
using LPR381.Core.Sensitivity;
using LPR381.Core.NonLinear;
using LPR381.Core.Simplex;

namespace LPR381.App.Menu;

/// <summary>
/// The menu driven front end required by the brief. It holds the model that is currently loaded and
/// the last result that was produced, and hands the work off to LPR381.Core.
/// </summary>
public sealed class MainMenu
{
    private LinearModel? _model;

    /// <summary>
    /// Set instead of <see cref="_model"/> when the input file holds a one-variable non-linear
    /// function. Exactly one of the two is ever loaded.
    /// </summary>
    private NonLinearModel? _nonLinearModel;

    private string? _modelPath;
    private SolveResult? _lastResult;
    private readonly List<string> _sensitivityLog = [];

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

        if (!File.Exists(path))
        {
            throw new ModelParseException(0, $"Input file not found: {Path.GetFullPath(path)}");
        }

        // Two input formats share this menu option. The file itself says which it is: a linear
        // objective is only signed numbers, so a letter on the objective line means it is a function.
        string[] lines = File.ReadAllLines(path);
        _model = null;
        _nonLinearModel = null;

        if (NonLinearFileParser.LooksNonLinear(lines))
        {
            _nonLinearModel = NonLinearFileParser.Parse(lines);
        }
        else
        {
            _model = ModelFileParser.Parse(lines);
        }

        _modelPath = Path.GetFullPath(path);
        _lastResult = null;
        _sensitivityLog.Clear();

        if (_nonLinearModel is not null)
        {
            ConsoleHelper.WriteSuccess(
                $"Loaded a non-linear model in one variable ({_nonLinearModel.VariableName}).");
            Console.WriteLine();
            Console.WriteLine(_nonLinearModel.Describe());
        }
        else
        {
            ConsoleHelper.WriteSuccess(
                $"Loaded {_model!.Variables.Count} decision variable(s) and {_model.Constraints.Count} constraint(s).");
            Console.WriteLine();
            Console.WriteLine(_model.Describe());
        }

        ConsoleHelper.Pause();
    }

    private void ViewModel()
    {
        if (!RequireModel())
        {
            return;
        }

        if (_nonLinearModel is not null)
        {
            // A curve has no canonical form and no starting table - there is nothing to convert.
            ConsoleHelper.WriteHeading("Model");
            Console.WriteLine();
            Console.WriteLine("PROGRAMMING MODEL");
            Console.WriteLine();
            Console.WriteLine(_nonLinearModel.Describe());
            Console.WriteLine();
            ConsoleHelper.WriteWarning(
                "This is a non-linear model, so there is no canonical form or starting table. The Golden " +
                "Section Search works on the function directly, evaluating it inside the interval.");
            ConsoleHelper.Pause();
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

        // A non-linear model has exactly one algorithm that fits it, so there is nothing to choose.
        if (_nonLinearModel is not null)
        {
            SolveNonLinear();
            return;
        }

        ConsoleHelper.WriteHeading("Choose an algorithm");
        Console.WriteLine();

        IReadOnlyList<ISolver> solvers = SolverRegistry.All;
        for (int i = 0; i < solvers.Count; i++)
        {
            ISolver solver = solvers[i];
            string tag = solver.CanSolve(_model!, out _) ? string.Empty : "   [not suited to this model]";
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
            ConsoleHelper.Pause();
            return;
        }

        if (reason.Length > 0)
        {
            ConsoleHelper.WriteWarning(reason);
        }

        SolveResult result = chosen.Solve(_model!);
        _lastResult = result;
        _sensitivityLog.Clear();

        ConsoleHelper.WriteHeading($"{chosen.Name} - working");
        Console.WriteLine();
        ReportWriter.Write(Console.Out, _model!, result);

        if (result.IsOptimal)
        {
            ConsoleHelper.WriteSuccess("Solved. Use option 5 to export this working to an output file.");
        }

        ConsoleHelper.Pause();
    }

    /// <summary>
    /// The bonus criterion. A non-linear model goes straight to the Golden Section Search - it is the
    /// only algorithm here that works on a curve rather than a table.
    /// </summary>
    private void SolveNonLinear()
    {
        var solver = new GoldenSectionSolver();
        SolveResult result = solver.Solve(_nonLinearModel!);
        _lastResult = result;
        _sensitivityLog.Clear();

        ConsoleHelper.WriteHeading($"{solver.Name} - working");
        Console.WriteLine();
        ReportWriter.Write(Console.Out, _nonLinearModel!, result);

        if (result.IsOptimal)
        {
            ConsoleHelper.WriteSuccess("Solved. Use option 5 to export this working to an output file.");
        }

        ConsoleHelper.Pause();
    }

    private void SensitivityAnalysis()
    {
        if (!RequireModel())
        {
            return;
        }

        if (_nonLinearModel is not null)
        {
            ConsoleHelper.WriteError(
                "Sensitivity analysis is defined on a simplex table, and the Golden Section Search does " +
                "not build one. Load a linear model and solve it with the Primal or Revised Simplex.");
            ConsoleHelper.Pause();
            return;
        }

        if (_lastResult is null || !_lastResult.IsOptimal)
        {
            ConsoleHelper.WriteError("Solve the model to optimality first (option 3), then come back here.");
            ConsoleHelper.Pause();
            return;
        }

        if (!_lastResult.SupportsSensitivityAnalysis)
        {
            ConsoleHelper.WriteWarning(
                $"Sensitivity analysis is not available for the {_lastResult.AlgorithmName} result. " +
                "It requires the final optimal simplex table from Primal Simplex or Revised Primal Simplex. " +
                "Branch & Bound, Cutting Plane and Knapsack B&B produce integer-search results rather than " +
                "a directly analysable LP basis.");
            ConsoleHelper.Pause();
            return;
        }

        ConsoleHelper.WriteHeading("Sensitivity analysis");
        Console.WriteLine();

        IReadOnlyList<ISensitivityOperation> operations = SensitivityRegistry.All;
        for (int i = 0; i < operations.Count; i++)
        {
            Console.WriteLine($"  {i + 1,2}. {operations[i].Name}");
            Console.WriteLine($"      {operations[i].Description}");
        }

        Console.WriteLine("   0. Back");
        Console.WriteLine();

        string answer = ConsoleHelper.Prompt("  Select an operation: ");
        if (!int.TryParse(answer, out int choice) || choice < 1 || choice > operations.Count)
        {
            return;
        }

        ISensitivityOperation operation = operations[choice - 1];
        ConsoleHelper.WriteHeading(operation.Name);
        Console.WriteLine();

        string output = operation.Execute(_lastResult, new ConsoleSensitivityInput());

        Console.WriteLine();
        Console.WriteLine(output);

        // Kept so option 5 can export the analysis along with the algorithm's working.
        _sensitivityLog.Add($"{operation.Name}{Environment.NewLine}{new string('-', operation.Name.Length)}" +
                            $"{Environment.NewLine}{Environment.NewLine}{output}");

        ConsoleHelper.WriteSuccess("This analysis will be included when you export with option 5.");
        ConsoleHelper.Pause();
    }

    private void ExportResults()
    {
        if ((_model is null && _nonLinearModel is null) || _lastResult is null)
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
            if (_nonLinearModel is not null)
            {
                ReportWriter.Write(writer, _nonLinearModel, _lastResult, _sensitivityLog);
            }
            else
            {
                ReportWriter.Write(writer, _model!, _lastResult, _sensitivityLog);
            }
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
        Console.WriteLine("  Lines starting with # or // are ignored, so files can be annotated.");
        Console.WriteLine();
        Console.WriteLine("  NON-LINEAR (bonus criterion) - a different, shorter format:");
        Console.WriteLine();
        Console.WriteLine("  Line 1      : max or min, then f written in terms of one variable, x.");
        Console.WriteLine("  Line 2      : the interval to search.");
        Console.WriteLine("  Line 3      : optional stopping tolerance (default 0.001).");
        Console.WriteLine();
        Console.WriteLine("      max 4sin(x)(1 + cos(x))");
        Console.WriteLine("      0 <= x <= 1.5707963268");
        Console.WriteLine("      tolerance 0.05");
        Console.WriteLine();
        Console.WriteLine("  You may write + - * / ^ and brackets, the functions sin cos tan sqrt exp");
        Console.WriteLine("  ln log abs, and the constants pi and e. A file like this is solved by the");
        Console.WriteLine("  Golden Section Search. The program tells the two formats apart on its own:");
        Console.WriteLine("  a linear objective line is only signed numbers, so a letter means non-linear.");
        ConsoleHelper.Pause();
    }

    private bool RequireModel()
    {
        if (_model is not null || _nonLinearModel is not null)
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
