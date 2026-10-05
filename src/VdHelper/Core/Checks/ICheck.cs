using VdHelper.Core.Model;

namespace VdHelper.Core.Checks;

/// <summary>
/// One diagnostic check. Checks are data + an executor: adding a failure mode means adding a
/// check, not touching the UI (ADR-002).
/// </summary>
public interface ICheck
{
    CheckDefinition Definition { get; }

    Task<CheckResult> RunAsync(CancellationToken ct = default);
}

public static class CheckFactory
{
    public static ICheck Delegate(CheckDefinition definition, Func<CancellationToken, Task<CheckResult>> run)
        => new DelegateCheck(definition, run);

    private sealed class DelegateCheck(CheckDefinition definition, Func<CancellationToken, Task<CheckResult>> run) : ICheck
    {
        public CheckDefinition Definition { get; } = definition;
        public Task<CheckResult> RunAsync(CancellationToken ct = default) => run(ct);
    }
}