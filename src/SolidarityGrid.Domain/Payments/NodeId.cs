namespace SolidarityGrid.Domain.Payments;

public sealed record NodeId
{
    public const int MaximumLength = 64;

    public NodeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumLength ||
            value.Any(character => !IsAllowedCharacter(character)))
        {
            throw new PaymentDomainException(
                PaymentErrorCodes.NodeIdInvalid,
                "The node ID must contain at most 64 letters, numbers, or hyphens.");
        }

        Value = value.ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;

    private static bool IsAllowedCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character == '-';
}
