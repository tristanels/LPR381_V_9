namespace LPR381.Core.Models;

/// <summary>Whether the objective function is maximised or minimised.</summary>
public enum ObjectiveSense
{
    Max,
    Min
}

/// <summary>The relation used by a constraint (Notes: "=, &lt;=, or &gt;=").</summary>
public enum Relation
{
    LessOrEqual,
    Equal,
    GreaterOrEqual
}

/// <summary>
/// The sign restriction placed on a decision variable. These come from the last line of the
/// input file: <c>+ - urs int bin</c>.
/// </summary>
public enum SignRestriction
{
    /// <summary>x >= 0 (the normal case).</summary>
    Positive,

    /// <summary>x &lt;= 0. Handled by substituting x = -x'.</summary>
    Negative,

    /// <summary>Unrestricted in sign. Handled by substituting x = x+ - x-.</summary>
    Unrestricted,

    /// <summary>x must be a non-negative integer. Relaxed for the LP, enforced by Branch &amp; Bound.</summary>
    Integer,

    /// <summary>x must be 0 or 1. Relaxed to 0 &lt;= x &lt;= 1 for the LP.</summary>
    Binary
}
