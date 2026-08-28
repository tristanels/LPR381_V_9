using System.Globalization;
using System.Text.RegularExpressions;
using LPR381.Core.Models;
using LPR381.Core.NonLinear;

namespace LPR381.Core.IO;

/// <summary>
/// Reads the input file for a non-linear model, the kind the Golden Section Search solves:
/// <code>
/// max 4sin(x)(1 + cos(x))
/// 0 &lt;= x &lt;= 1.5707963268
/// tolerance 0.05
/// </code>
/// Line 1 is <c>max</c> or <c>min</c> followed by the function. Line 2 is the interval to search.
/// Line 3 is optional and sets the stopping tolerance, defaulting to
/// <see cref="DefaultTolerance"/>. As in the linear format, <c>#</c> and <c>//</c> start a comment.
/// </summary>
public static class NonLinearFileParser
{
    /// <summary>Used when the file does not give a tolerance of its own.</summary>
    public const double DefaultTolerance = 0.001;

    /// <summary>Matches "0 &lt;= x &lt;= 1.57", and also the looser "0 to 1.57" and "0 1.57".</summary>
    private static readonly Regex IntervalPattern = new(
        @"^(?<low>[^<>=\s]+)\s*(?:<=|<|=<|to|,)?\s*(?:[A-Za-z]\w*)?\s*(?:<=|<|=<|to|,)?\s*(?<high>[^<>=\s]+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Decides which of the two input formats a file is in, so the menu can pick the right parser.
    /// The linear format's objective line is nothing but signed numbers, so a letter or a bracket
    /// after max/min means the objective is a function and the file is non-linear.
    /// </summary>
    public static bool LooksNonLinear(IReadOnlyList<string> rawLines)
    {
        ArgumentNullException.ThrowIfNull(rawLines);

        foreach (string raw in rawLines)
        {
            string line = StripComment(raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // The first line with anything on it is the objective in both formats.
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !IsSenseWord(parts[0]))
            {
                return false;
            }

            // A linear objective is nothing but signed numbers, so a letter is the giveaway: it can
            // only be the variable or a function name. Brackets and ^ are deliberately not used as
            // the test, because "max +1^2 +3" is a linear file with a power on a coefficient.
            string objective = string.Join(' ', parts[1..]);
            return objective.Any(char.IsLetter);
        }

        return false;
    }

    public static NonLinearModel ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new ModelParseException(0, $"Input file not found: {path}");
        }

        return Parse(File.ReadAllLines(path));
    }

    public static NonLinearModel ParseText(string text) =>
        Parse(text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray());

    public static NonLinearModel Parse(IReadOnlyList<string> rawLines)
    {
        ArgumentNullException.ThrowIfNull(rawLines);

        // Keep the original line numbers so errors point at the real file, as the linear parser does.
        List<(int Number, string Text)> lines = [];
        for (int i = 0; i < rawLines.Count; i++)
        {
            string text = StripComment(rawLines[i]).Trim();
            if (text.Length > 0)
            {
                lines.Add((i + 1, text));
            }
        }

        if (lines.Count == 0)
        {
            throw new ModelParseException(0, "The input file is empty.");
        }

        if (lines.Count < 2)
        {
            throw new ModelParseException(
                lines[0].Number,
                "A non-linear model needs a second line giving the interval to search, for example '0 <= x <= 3'.");
        }

        (ObjectiveSense sense, FunctionExpression function) = ParseObjective(lines[0]);
        (double lower, double upper) = ParseInterval(lines[1]);
        double tolerance = lines.Count > 2 ? ParseTolerance(lines[2]) : DefaultTolerance;

        if (lines.Count > 3)
        {
            throw new ModelParseException(
                lines[3].Number,
                "A non-linear model has at most three lines: the function, the interval and the tolerance.");
        }

        if (!(upper > lower))
        {
            throw new ModelParseException(
                lines[1].Number,
                $"The interval is empty: the upper bound ({upper}) must be above the lower bound ({lower}).");
        }

        return new NonLinearModel(sense, function, lower, upper, tolerance);
    }

    private static (ObjectiveSense Sense, FunctionExpression Function) ParseObjective((int Number, string Text) line)
    {
        string[] parts = line.Text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || !IsSenseWord(parts[0]))
        {
            throw new ModelParseException(
                line.Number,
                "The first line must be 'max' or 'min' followed by the function, for example 'max 4sin(x)(1 + cos(x))'.");
        }

        ObjectiveSense sense = parts[0].Equals("max", StringComparison.OrdinalIgnoreCase)
            ? ObjectiveSense.Max
            : ObjectiveSense.Min;

        // Allow the function to be written as "f(x) = ..." as well as bare.
        string body = parts[1];
        int equals = body.IndexOf('=');
        if (equals >= 0)
        {
            body = body[(equals + 1)..];
        }

        try
        {
            return (sense, FunctionExpression.Parse(body));
        }
        catch (FunctionParseException error)
        {
            throw new ModelParseException(line.Number, error.Message);
        }
    }

    private static (double Lower, double Upper) ParseInterval((int Number, string Text) line)
    {
        Match match = IntervalPattern.Match(line.Text);

        if (!match.Success)
        {
            throw new ModelParseException(
                line.Number,
                $"'{line.Text}' is not an interval. Write it as '0 <= x <= 3'.");
        }

        double lower = ParseNumber(match.Groups["low"].Value, line.Number, "lower bound");
        double upper = ParseNumber(match.Groups["high"].Value, line.Number, "upper bound");

        return (lower, upper);
    }

    private static double ParseTolerance((int Number, string Text) line)
    {
        string[] parts = line.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Accept "tolerance 0.05", "eps 0.05" and a bare "0.05".
        string value = parts.Length switch
        {
            1 => parts[0],
            2 when parts[0].StartsWith("tol", StringComparison.OrdinalIgnoreCase)
                || parts[0].StartsWith("eps", StringComparison.OrdinalIgnoreCase) => parts[1],
            _ => throw new ModelParseException(
                line.Number,
                $"'{line.Text}' is not a tolerance. Write it as 'tolerance 0.05'.")
        };

        double tolerance = ParseNumber(value, line.Number, "tolerance");

        if (!Num.IsPositive(tolerance))
        {
            throw new ModelParseException(line.Number, "The tolerance must be above zero.");
        }

        return tolerance;
    }

    private static double ParseNumber(string token, int lineNumber, string what)
    {
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new ModelParseException(lineNumber, $"The {what} '{token}' is not a number.");
        }

        return value;
    }

    private static bool IsSenseWord(string token) =>
        token.Equals("max", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("min", StringComparison.OrdinalIgnoreCase);

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        int slashes = line.IndexOf("//", StringComparison.Ordinal);

        int cut = hash >= 0 && slashes >= 0 ? Math.Min(hash, slashes) : Math.Max(hash, slashes);
        return cut >= 0 ? line[..cut] : line;
    }
}
