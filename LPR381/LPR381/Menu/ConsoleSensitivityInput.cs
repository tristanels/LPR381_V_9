using System.Globalization;
using LPR381.Core.Sensitivity;

namespace LPR381.App.Menu;

/// <summary>
/// Asks the user for whatever a sensitivity operation needs. This is the only place the operations
/// touch the console - LPR381.Core just calls back through <see cref="ISensitivityInput"/>.
/// </summary>
public sealed class ConsoleSensitivityInput : ISensitivityInput
{
    public bool TryReadNumber(string prompt, out double value)
    {
        while (true)
        {
            string answer = ConsoleHelper.Prompt("  " + prompt);

            if (answer.Length == 0)
            {
                value = 0.0;
                return false;
            }

            if (double.TryParse(answer, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            ConsoleHelper.WriteError($"'{answer}' is not a number. Enter a number, or press ENTER to cancel.");
        }
    }

    public int ReadChoice(string prompt, IReadOnlyList<string> options)
    {
        if (options.Count == 0)
        {
            ConsoleHelper.WriteWarning("There is nothing to choose from here.");
            return -1;
        }

        Console.WriteLine();
        Console.WriteLine("  " + prompt);
        for (int i = 0; i < options.Count; i++)
        {
            Console.WriteLine($"    {i + 1}. {options[i]}");
        }

        while (true)
        {
            string answer = ConsoleHelper.Prompt("  Choice (ENTER to cancel): ");

            if (answer.Length == 0)
            {
                return -1;
            }

            if (int.TryParse(answer, out int choice) && choice >= 1 && choice <= options.Count)
            {
                return choice - 1;
            }

            ConsoleHelper.WriteError($"Enter a number from 1 to {options.Count}, or press ENTER to cancel.");
        }
    }
}
