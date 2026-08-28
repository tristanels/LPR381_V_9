using System.Globalization;
using LPR381.Core.Models;

namespace LPR381.Core.NonLinear;

/// <summary>
/// A function of one variable, read from the input file as ordinary maths and evaluated on demand.
/// <para>
/// The Golden Section Search (study notes section 18) never differentiates the function - it only ever
/// asks "what is f at this point?". So all this class has to do is turn a line of text such as
/// <c>4sin(x)(1 + cos(x))</c> into something that can answer that question.
/// </para>
/// <para>
/// <b>How the parsing works.</b> Two passes, the standard way of reading maths:
/// </para>
/// <list type="number">
/// <item><b>Tokenise</b> - chop the text into numbers, names, operators and brackets.</item>
/// <item><b>Recursive descent</b> - one method per precedence level, each calling the level below it.
/// <c>Sum</c> handles + and -, which bind loosest; it calls <c>Product</c> for * and /; that calls
/// <c>Power</c> for ^; which calls <c>Atom</c> for numbers, x, brackets and sin/cos/... The nesting of
/// the calls is what gives the operators their precedence, so there is no precedence table to keep.</item>
/// </list>
/// <para>
/// Each parse method returns a <see cref="Func{Double, Double}"/> - a small piece of the finished
/// calculation. They compose into one delegate, so evaluating is just calling it.
/// </para>
/// </summary>
public sealed class FunctionExpression
{
    /// <summary>Names that may appear in a function, mapped to what they work out.</summary>
    private static readonly Dictionary<string, Func<double, double>> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sin"] = Math.Sin,
        ["cos"] = Math.Cos,
        ["tan"] = Math.Tan,
        ["sqrt"] = Math.Sqrt,
        ["exp"] = Math.Exp,
        ["ln"] = Math.Log,
        ["log"] = Math.Log10,
        ["abs"] = Math.Abs
    };

    private static readonly Dictionary<string, double> Constants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pi"] = Math.PI,
        ["e"] = Math.E
    };

    private readonly Func<double, double> _evaluate;

    private FunctionExpression(string text, string variableName, Func<double, double> evaluate)
    {
        Text = text;
        VariableName = variableName;
        _evaluate = evaluate;
    }

    /// <summary>The function exactly as it was written in the input file.</summary>
    public string Text { get; }

    /// <summary>The name the function is written in terms of, normally <c>x</c>.</summary>
    public string VariableName { get; }

    /// <summary>Works out f at a point. This is the only thing the search ever needs.</summary>
    public double Evaluate(double x)
    {
        double value = _evaluate(x);

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new FunctionParseException(
                $"f({Num.Format(x)}) could not be worked out - the function is not defined there. " +
                "Check the interval, for example ln(x) needs x > 0.");
        }

        return value;
    }

    /// <summary>Reads a function such as <c>4sin(x)(1 + cos(x))</c> or <c>-x^2 + 4x</c>.</summary>
    /// <exception cref="FunctionParseException">The text is not a function this class can read.</exception>
    public static FunctionExpression Parse(string text, string variableName = "x")
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FunctionParseException("The function is blank. Write something like 4sin(x)(1 + cos(x)).");
        }

        List<Token> tokens = Tokenise(text, variableName);
        var parser = new Parser(tokens, variableName);
        Func<double, double> evaluate = parser.ParseWholeExpression();

        return new FunctionExpression(text.Trim(), variableName, evaluate);
    }

    // ----- pass 1: tokenising ------------------------------------------------------------------

    private enum TokenKind
    {
        Number,
        Variable,
        Function,
        Operator,
        Open,
        Close
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Number);

    /// <summary>
    /// Chops the text into tokens. The only fiddly part is a run of letters: the notes write
    /// <c>4sinx</c> with no brackets, so a run like "sinx" has to come apart into "sin" and "x".
    /// </summary>
    private static List<Token> Tokenise(string text, string variableName)
    {
        List<Token> tokens = [];
        int position = 0;

        while (position < text.Length)
        {
            char current = text[position];

            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }

            if (char.IsDigit(current) || current == '.')
            {
                int start = position;
                while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.'))
                {
                    position++;
                }

                string number = text[start..position];
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw new FunctionParseException($"'{number}' is not a number I can read.");
                }

                tokens.Add(new Token(TokenKind.Number, number, value));
                continue;
            }

            if (char.IsLetter(current))
            {
                int start = position;
                while (position < text.Length && char.IsLetter(text[position]))
                {
                    position++;
                }

                AddLetterRun(tokens, text[start..position], variableName);
                continue;
            }

            switch (current)
            {
                case '(':
                case '[':
                    tokens.Add(new Token(TokenKind.Open, "(", 0));
                    position++;
                    continue;

                case ')':
                case ']':
                    tokens.Add(new Token(TokenKind.Close, ")", 0));
                    position++;
                    continue;

                case '+':
                case '-':
                case '*':
                case '/':
                case '^':
                    tokens.Add(new Token(TokenKind.Operator, current.ToString(), 0));
                    position++;
                    continue;

                default:
                    throw new FunctionParseException(
                        $"'{current}' does not belong in a function. Use + - * / ^ ( ), a number, " +
                        $"{variableName}, or one of {string.Join(", ", Functions.Keys)}.");
            }
        }

        if (tokens.Count == 0)
        {
            throw new FunctionParseException("The function is empty.");
        }

        return tokens;
    }

    /// <summary>
    /// Splits a run of letters into the longest known names, so "sinx" reads as sin(x) and "cosx" as
    /// cos(x) - the way the study notes write them.
    /// </summary>
    private static void AddLetterRun(List<Token> tokens, string run, string variableName)
    {
        int index = 0;

        while (index < run.Length)
        {
            string remainder = run[index..];

            if (remainder.StartsWith(variableName, StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token(TokenKind.Variable, variableName, 0));
                index += variableName.Length;
                continue;
            }

            // Longest match first, so "exp" is not mistaken for "e" followed by "xp".
            string? name = Functions.Keys
                .Concat(Constants.Keys)
                .Where(candidate => remainder.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.Length)
                .FirstOrDefault();

            if (name is null)
            {
                throw new FunctionParseException(
                    $"I do not recognise '{remainder}' in the function. Use {variableName}, a number, " +
                    $"or one of {string.Join(", ", Functions.Keys)}.");
            }

            tokens.Add(Functions.ContainsKey(name)
                ? new Token(TokenKind.Function, name, 0)
                : new Token(TokenKind.Number, name, Constants[name]));

            index += name.Length;
        }
    }

    // ----- pass 2: recursive descent -----------------------------------------------------------

    /// <summary>
    /// One method per precedence level. Each returns a delegate that works out its part of the
    /// function, and they call each other, so the shape of the calls is the order of operations.
    /// </summary>
    private sealed class Parser(List<Token> tokens, string variableName)
    {
        private int _position;

        private Token Current => tokens[_position];

        private bool AtEnd => _position >= tokens.Count;

        public Func<double, double> ParseWholeExpression()
        {
            Func<double, double> result = Sum();

            if (!AtEnd)
            {
                throw new FunctionParseException(
                    $"I could not read the function past '{Current.Text}'. Check the brackets.");
            }

            return result;
        }

        /// <summary>Addition and subtraction - the loosest binding, so the outermost level.</summary>
        private Func<double, double> Sum()
        {
            Func<double, double> left = Product();

            while (!AtEnd && Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
            {
                string op = Current.Text;
                _position++;
                Func<double, double> right = Product();
                Func<double, double> first = left;

                left = op == "+"
                    ? x => first(x) + right(x)
                    : x => first(x) - right(x);
            }

            return left;
        }

        /// <summary>
        /// Multiplication and division. Two numbers or brackets written side by side with no operator
        /// between them - 4sin(x), 2x, (1+x)(2-x) - are a multiplication too, which is how the notes
        /// write things.
        /// </summary>
        private Func<double, double> Product()
        {
            Func<double, double> left = Unary();

            while (!AtEnd)
            {
                bool explicitOperator = Current.Kind == TokenKind.Operator && Current.Text is "*" or "/";
                bool implicitProduct = Current.Kind is TokenKind.Number or TokenKind.Variable
                    or TokenKind.Function or TokenKind.Open;

                if (!explicitOperator && !implicitProduct)
                {
                    break;
                }

                string op = "*";
                if (explicitOperator)
                {
                    op = Current.Text;
                    _position++;
                }

                Func<double, double> right = Unary();
                Func<double, double> first = left;

                left = op == "*"
                    ? x => first(x) * right(x)
                    : x => first(x) / right(x);
            }

            return left;
        }

        /// <summary>
        /// A leading minus. This sits <i>above</i> the power level, because -x^2 means -(x^2) and not
        /// (-x)^2 - the minus binds loosest of the three.
        /// </summary>
        private Func<double, double> Unary()
        {
            if (!AtEnd && Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
            {
                string op = Current.Text;
                _position++;
                Func<double, double> operand = Unary();
                return op == "-" ? x => -operand(x) : operand;
            }

            return Power();
        }

        /// <summary>
        /// Powers. x^2^3 means x^(2^3), so the right hand side is parsed from the unary level down -
        /// which also lets the exponent carry its own sign, as in 2^-3.
        /// </summary>
        private Func<double, double> Power()
        {
            Func<double, double> baseValue = Atom();

            if (!AtEnd && Current.Kind == TokenKind.Operator && Current.Text == "^")
            {
                _position++;
                Func<double, double> exponent = Unary();
                return x => Math.Pow(baseValue(x), exponent(x));
            }

            return baseValue;
        }

        /// <summary>A number, the variable, a bracketed expression, or a function applied to one.</summary>
        private Func<double, double> Atom()
        {
            if (AtEnd)
            {
                throw new FunctionParseException("The function stops in the middle - something is missing at the end.");
            }

            Token token = Current;

            switch (token.Kind)
            {
                case TokenKind.Number:
                    _position++;
                    return _ => token.Number;

                case TokenKind.Variable:
                    _position++;
                    return x => x;

                case TokenKind.Function:
                {
                    _position++;
                    Func<double, double> function = Functions[token.Text];

                    // The notes write "sin x" as well as "sin(x)", so the argument is whatever comes
                    // next at power level - that keeps sin x^2 meaning sin(x^2).
                    Func<double, double> argument = Power();
                    return x => function(argument(x));
                }

                case TokenKind.Open:
                {
                    _position++;
                    Func<double, double> inner = Sum();

                    if (AtEnd || Current.Kind != TokenKind.Close)
                    {
                        throw new FunctionParseException("A closing bracket ')' is missing.");
                    }

                    _position++;
                    return inner;
                }

                default:
                    throw new FunctionParseException(
                        $"'{token.Text}' is not where I expected a number, {variableName} or a bracket.");
            }
        }
    }
}

/// <summary>Thrown when a function in the input file cannot be read or cannot be evaluated.</summary>
public sealed class FunctionParseException(string message) : Exception(message);
