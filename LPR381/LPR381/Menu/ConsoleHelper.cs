namespace LPR381.App.Menu;

/// <summary>Small helpers that keep the menu code readable and the screen output consistent.</summary>
public static class ConsoleHelper
{
    public static void WriteHeading(string text)
    {
        Console.WriteLine();
        WriteInColour(ConsoleColor.Cyan, new string('=', 70));
        WriteInColour(ConsoleColor.Cyan, "  " + text);
        WriteInColour(ConsoleColor.Cyan, new string('=', 70));
    }

    public static void WriteError(string text) => WriteInColour(ConsoleColor.Red, "  " + text);

    public static void WriteSuccess(string text) => WriteInColour(ConsoleColor.Green, "  " + text);

    public static void WriteWarning(string text) => WriteInColour(ConsoleColor.Yellow, "  " + text);

    public static void WriteInColour(ConsoleColor colour, string text)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = colour;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    /// <summary>Asks a question and returns what was typed, trimmed. Never returns null.</summary>
    public static string Prompt(string question)
    {
        Console.Write(question);
        return (Console.ReadLine() ?? string.Empty).Trim();
    }

    public static void Pause()
    {
        Console.WriteLine();
        Console.Write("  Press ENTER to continue...");
        Console.ReadLine();
    }
}
