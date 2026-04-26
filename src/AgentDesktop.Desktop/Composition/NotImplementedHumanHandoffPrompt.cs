using AgentDesktop.Application.Abstractions;

namespace AgentDesktop.Desktop.Composition;

/// <summary>
/// Placeholder until the Phase-5 <c>HumanHandoffDialog</c> (T101) ships.
/// Throws if a delegation actually requests a hand-off — US1/US3 do not
/// exercise this path, so this keeps DI valid without faking behaviour.
/// </summary>
internal sealed class NotImplementedHumanHandoffPrompt : IHumanHandoffPrompt
{
    public Task WaitForCompletionAsync(string stepName, string instructions, CancellationToken ct)
    {
        throw new NotSupportedException(
            $"Human-handoff prompt is not wired yet (T101). Requested step: '{stepName}'.");
    }
}
