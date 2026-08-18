using LPR381.Core.Models;

namespace LPR381.Core.Algorithms;

/// <summary>
/// One algorithm the user can pick from the menu. Every algorithm in the brief implements this
/// interface, so the menu is built from <see cref="SolverRegistry"/> and finishing an outstanding
/// algorithm means filling in <see cref="Solve"/> - nothing else in the program has to change.
/// </summary>
public interface ISolver
{
    /// <summary>Name shown in the menu and written at the top of the output file.</summary>
    string Name { get; }

    /// <summary>One line describing what the algorithm does and when to use it.</summary>
    string Description { get; }

    /// <summary>True once the algorithm is actually implemented (false for the placeholders).</summary>
    bool IsImplemented { get; }

    /// <summary>
    /// Checks up front whether this algorithm suits the model, so the user gets an explanation
    /// instead of a wrong answer (for example, Knapsack B&amp;B needs exactly one constraint).
    /// </summary>
    bool CanSolve(LinearModel model, out string reason);

    SolveResult Solve(LinearModel model);
}
