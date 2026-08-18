namespace LPR381.Core.IO;

/// <summary>
/// Thrown when an input file does not match the format the brief specifies. The message is written
/// to be shown straight to the user, so it always says which line failed and what was expected.
/// </summary>
public sealed class ModelParseException : Exception
{
    public ModelParseException(int lineNumber, string message)
        : base(lineNumber > 0 ? $"Line {lineNumber}: {message}" : message)
    {
        LineNumber = lineNumber;
    }

    /// <summary>1-based line number in the input file, or 0 when the problem is with the file as a whole.</summary>
    public int LineNumber { get; }
}
