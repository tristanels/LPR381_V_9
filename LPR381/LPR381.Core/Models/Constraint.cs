namespace LPR381.Core.Models;

/// <summary>
/// One constraint line of the model: technological coefficients, a relation and a right-hand-side.
/// </summary>
public sealed class Constraint
{
    public Constraint(string name, IEnumerable<double> coefficients, Relation relation, double rightHandSide)
    {
        Name = name;
        Coefficients = coefficients.ToArray();
        Relation = relation;
        RightHandSide = rightHandSide;
    }

    /// <summary>Display name, e.g. "c1".</summary>
    public string Name { get; }

    /// <summary>One coefficient per decision variable, in the order the objective function declared them.</summary>
    public IReadOnlyList<double> Coefficients { get; }

    public Relation Relation { get; }

    public double RightHandSide { get; }

    public string RelationSymbol => Relation switch
    {
        Relation.LessOrEqual => "<=",
        Relation.GreaterOrEqual => ">=",
        Relation.Equal => "=",
        _ => "?"
    };
}
