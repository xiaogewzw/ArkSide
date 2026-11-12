namespace GameSidebar.Application.Recognition;

public sealed record OperatorRecognitionResult(
    IReadOnlyList<string> DisabledOperators,
    int TotalOperators,
    int AvailableOperators,
    IReadOnlyList<string> DisabledMainCovenants,
    IReadOnlyList<string> DisabledViceCovenants);

public interface IOperatorRecognizer
{
    Task<OperatorRecognitionResult> RecognizeAsync(byte[] png, CancellationToken cancellationToken);
}
