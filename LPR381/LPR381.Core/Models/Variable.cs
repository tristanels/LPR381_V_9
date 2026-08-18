namespace LPR381.Core.Models;

/// <summary>A decision variable of the programming model, exactly as it was written in the input file.</summary>
public sealed class Variable
{
    public Variable(
        string name,
        double objectiveCoefficient,
        SignRestriction signRestriction,
        double objectivePower = 1.0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A variable must have a name.", nameof(name));
        }

        Name = name;
        ObjectiveCoefficient = objectiveCoefficient;
        SignRestriction = signRestriction;
        ObjectivePower = objectivePower;
    }

    /// <summary>Display name, e.g. "x1".</summary>
    public string Name { get; }

    /// <summary>The coefficient this variable has in the objective function.</summary>
    public double ObjectiveCoefficient { get; }

    public SignRestriction SignRestriction { get; }

    /// <summary>
    /// The power the variable is raised to in the objective function, from the optional ^p suffix on
    /// its coefficient. 1 for a normal linear term; anything else makes the model non-linear.
    /// </summary>
    public double ObjectivePower { get; }

    /// <summary>True when this term is not linear, e.g. 2x1^2.</summary>
    public bool IsNonLinear => !Num.AreEqual(ObjectivePower, 1.0);

    /// <summary>True when the variable may not take a fractional value in the final answer.</summary>
    public bool IsDiscrete =>
        SignRestriction is SignRestriction.Integer or SignRestriction.Binary;

    public override string ToString() => $"{Name} ({SignRestriction})";
}
