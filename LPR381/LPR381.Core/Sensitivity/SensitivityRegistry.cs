namespace LPR381.Core.Sensitivity;

/// <summary>
/// Every sensitivity analysis operation the brief asks for, in the order it lists them. The
/// sensitivity menu is generated from this list.
/// </summary>
public static class SensitivityRegistry
{
    public static IReadOnlyList<ISensitivityOperation> All { get; } =
    [
        new NonBasicVariableRangeOperation(),
        new NonBasicVariableChangeOperation(),
        new BasicVariableRangeOperation(),
        new BasicVariableChangeOperation(),
        new RhsRangeOperation(),
        new RhsChangeOperation(),
        new NonBasicColumnCoefficientRangeOperation(),
        new NonBasicColumnCoefficientChangeOperation(),
        new AddActivityOperation(),
        new AddConstraintOperation(),
        new ShadowPriceOperation(),
        new DualityOperation()
    ];
}
