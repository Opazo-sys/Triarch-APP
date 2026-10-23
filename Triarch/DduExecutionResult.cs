namespace Triarch;

public sealed record DduExecutionResult(bool Started, bool CancelledByUac, string Message);
