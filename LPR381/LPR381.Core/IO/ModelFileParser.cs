using System.Globalization;
using System.Text.RegularExpressions;
using LPR381.Core.Models;

namespace LPR381.Core.IO;

/// <summary>
/// Reads an input text file in the format required by the brief:
/// <code>
/// max +2 +3 +3 +5 +2 +4
/// +11 +8 +6 +14 +10 +10 &lt;=40
/// bin bin bin bin bin bin
/// </code>
/// Line 1 is the objective (max/min plus one signed coefficient per decision variable), every line
/// after it is a constraint, and the final line holds one sign restriction per decision variable.
/// Spacing is treated leniently: "+2" and "+ 2", "&lt;=40" and "&lt;= 40" all parse.
/// </summary>
public static class ModelFileParser
{
    private static readonly string[] SignRestrictionWords = ["+", "-", "urs", "int", "bin"];

    /// <summary>Splits a constraint line into left-hand side, relation and right-hand side.</summary>
    private static readonly Regex RelationPattern =
        new(@"^(?<lhs>.*?)(?<rel><=|>=|=<|=>|=|<|>)(?<rhs>[^<>=]*)$", RegexOptions.Compiled);

    public static LinearModel ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new ModelParseException(0, $"Input file not found: {path}");
        }

        return Parse(File.ReadAllLines(path));
    }

    public static LinearModel ParseText(string text) =>
        Parse(text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray());

    public static LinearModel Parse(IReadOnlyList<string> rawLines)
    {
        // Keep the original line numbers so error messages point at the real file.
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

        (ObjectiveSense sense, double[] objectiveCoefficients, double[] powers) = ParseObjective(lines[0]);
        int variableCount = objectiveCoefficients.Length;

        // The sign restriction line is the last line, and is recognisable because every token on it
        // is one of + - urs int bin.
        (int Number, string Text) lastLine = lines[^1];
        if (lines.Count < 2 || !IsSignRestrictionLine(lastLine.Text))
        {
            throw new ModelParseException(
                lastLine.Number,
                "The last line must hold the sign restrictions (+, -, urs, int or bin), one per decision variable.");
        }

        SignRestriction[] restrictions = ParseSignRestrictions(lastLine, variableCount);

        List<Constraint> constraints = [];
        for (int i = 1; i < lines.Count - 1; i++)
        {
            constraints.Add(ParseConstraint(lines[i], constraints.Count + 1, variableCount));
        }

        if (constraints.Count == 0)
        {
            throw new ModelParseException(0, "The model has no constraints. Add at least one constraint line.");
        }

        Variable[] variables = new Variable[variableCount];
        for (int i = 0; i < variableCount; i++)
        {
            variables[i] = new Variable($"x{i + 1}", objectiveCoefficients[i], restrictions[i], powers[i]);
        }

        return new LinearModel(sense, variables, constraints);
    }

    private static (ObjectiveSense Sense, double[] Coefficients, double[] Powers) ParseObjective(
        (int Number, string Text) line)
    {
        List<string> tokens = Tokenise(line.Text);

        string keyword = tokens[0].ToLowerInvariant();
        ObjectiveSense sense = keyword switch
        {
            "max" => ObjectiveSense.Max,
            "min" => ObjectiveSense.Min,
            _ => throw new ModelParseException(
                line.Number,
                $"The first word must be 'max' or 'min' but was '{tokens[0]}'.")
        };

        if (tokens.Count < 2)
        {
            throw new ModelParseException(
                line.Number,
                "The objective function needs at least one coefficient after 'max' or 'min'.");
        }

        double[] coefficients = new double[tokens.Count - 1];
        double[] powers = new double[tokens.Count - 1];

        for (int i = 1; i < tokens.Count; i++)
        {
            // An objective coefficient may carry an optional ^p suffix for the non-linear bonus
            // criterion: "+2^2" means the term 2 * x^2. Without it the term is linear.
            string[] parts = tokens[i].Split('^');
            coefficients[i - 1] = ParseNumber(parts[0], line.Number, $"objective function coefficient {i}");
            powers[i - 1] = parts.Length > 1
                ? ParseNumber(parts[1], line.Number, $"power of objective function term {i}")
                : 1.0;

            if (parts.Length > 2)
            {
                throw new ModelParseException(line.Number, $"'{tokens[i]}' has more than one ^ in it.");
            }
        }

        return (sense, coefficients, powers);
    }

    private static Constraint ParseConstraint((int Number, string Text) line, int index, int variableCount)
    {
        Match match = RelationPattern.Match(line.Text);
        if (!match.Success)
        {
            throw new ModelParseException(
                line.Number,
                "A constraint must contain a relation (<=, >= or =) followed by the right-hand-side.");
        }

        List<string> coefficientTokens = Tokenise(match.Groups["lhs"].Value);
        if (coefficientTokens.Count != variableCount)
        {
            throw new ModelParseException(
                line.Number,
                $"This constraint has {coefficientTokens.Count} technological coefficient(s) but the " +
                $"objective function declared {variableCount} decision variable(s).");
        }

        double[] coefficients = new double[variableCount];
        for (int i = 0; i < variableCount; i++)
        {
            coefficients[i] = ParseNumber(coefficientTokens[i], line.Number, $"technological coefficient {i + 1}");
        }

        Relation relation = match.Groups["rel"].Value switch
        {
            "<=" or "=<" or "<" => Relation.LessOrEqual,
            ">=" or "=>" or ">" => Relation.GreaterOrEqual,
            "=" => Relation.Equal,
            _ => throw new ModelParseException(line.Number, "Unknown relation. Use <=, >= or =.")
        };

        string rhsText = match.Groups["rhs"].Value.Trim();
        if (rhsText.Length == 0)
        {
            throw new ModelParseException(line.Number, "The constraint is missing its right-hand-side value.");
        }

        double rightHandSide = ParseNumber(
            string.Concat(Tokenise(rhsText)),
            line.Number,
            "right-hand-side");

        return new Constraint($"c{index}", coefficients, relation, rightHandSide);
    }

    private static SignRestriction[] ParseSignRestrictions((int Number, string Text) line, int variableCount)
    {
        string[] tokens = line.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length != variableCount)
        {
            throw new ModelParseException(
                line.Number,
                $"There are {tokens.Length} sign restriction(s) but the model has {variableCount} decision variable(s).");
        }

        SignRestriction[] restrictions = new SignRestriction[variableCount];
        for (int i = 0; i < tokens.Length; i++)
        {
            restrictions[i] = tokens[i].ToLowerInvariant() switch
            {
                "+" => SignRestriction.Positive,
                "-" => SignRestriction.Negative,
                "urs" => SignRestriction.Unrestricted,
                "int" => SignRestriction.Integer,
                "bin" => SignRestriction.Binary,
                _ => throw new ModelParseException(
                    line.Number,
                    $"'{tokens[i]}' is not a sign restriction. Use +, -, urs, int or bin.")
            };
        }

        return restrictions;
    }

    /// <summary>
    /// Splits on whitespace, then glues a lone "+" or "-" onto the number that follows it, so that
    /// "+ 2" and "+2" are read the same way.
    /// </summary>
    private static List<string> Tokenise(string text)
    {
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        List<string> tokens = [];

        for (int i = 0; i < parts.Length; i++)
        {
            if ((parts[i] == "+" || parts[i] == "-") && i + 1 < parts.Length)
            {
                tokens.Add(parts[i] + parts[i + 1]);
                i++;
            }
            else
            {
                tokens.Add(parts[i]);
            }
        }

        return tokens;
    }

    private static bool IsSignRestrictionLine(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(token => SignRestrictionWords.Contains(token.ToLowerInvariant()));

    private static double ParseNumber(string token, int lineNumber, string what)
    {
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new ModelParseException(lineNumber, $"'{token}' is not a valid number for the {what}.");
        }

        return value;
    }

    /// <summary>Anything after # or // is ignored, so sample files can be annotated.</summary>
    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        int slashes = line.IndexOf("//", StringComparison.Ordinal);

        int cut = hash >= 0 && slashes >= 0 ? Math.Min(hash, slashes) : Math.Max(hash, slashes);
        return cut >= 0 ? line[..cut] : line;
    }
}
