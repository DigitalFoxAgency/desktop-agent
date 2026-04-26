using AgentDesktop.Desktop.Adapters;
using AgentDesktop.Desktop.Views;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Policies;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AgentDesktop.Desktop.Tests.Views;

/// <summary>
/// Headless a11y coverage for <see cref="ConfirmationDialog"/> (T077):
/// keyboard-only confirm and decline paths, focus trapped to the
/// confirm button on open, automation names exposed for screen
/// readers, and the action's target text is selectable so users can
/// read or copy it.
/// </summary>
public sealed class ConfirmationDialogTests
{
    [AvaloniaFact]
    public void Dialog_exposes_target_and_kind_to_screen_reader()
    {
        var action = SampleAction(DangerousActionKind.DeleteFile, "/tmp/important.txt");
        var (dialog, owner) = OpenDialog(action);
        try
        {
            dialog.FindControl<TextBlock>("KindText")!.Text.Should().Be("DeleteFile");
            dialog.FindControl<SelectableTextBlock>("TargetText")!.Text.Should().Be("/tmp/important.txt");

            var confirm = dialog.FindControl<Button>("ConfirmButton")!;
            var decline = dialog.FindControl<Button>("DeclineButton")!;

            AutomationProperties.GetName(confirm).Should().Be("Confirm action");
            AutomationProperties.GetName(decline).Should().Be("Decline action");
            AutomationProperties.GetName(dialog).Should().Be("Dangerous action confirmation");
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void Confirm_button_receives_focus_on_open()
    {
        var (dialog, owner) = OpenDialog(SampleAction(DangerousActionKind.GitPush, "origin/main"));
        try
        {
            var confirm = dialog.FindControl<Button>("ConfirmButton")!;
            confirm.IsFocused.Should().BeTrue("default button must hold focus so Enter immediately confirms");
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void Enter_key_confirms_via_default_button()
    {
        var (dialog, owner) = OpenDialog(SampleAction(DangerousActionKind.RunShell, "rm -rf /tmp/x"));
        try
        {
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            dialog.Result.Should().BeTrue();
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void Escape_key_declines_via_cancel_button()
    {
        var (dialog, owner) = OpenDialog(SampleAction(DangerousActionKind.InstallPackage, "left-pad@1.3.0"));
        try
        {
            dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            dialog.Result.Should().BeFalse();
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void Avalonia_prompt_serialises_concurrent_requests()
    {
        // Two concurrent ConfirmAsync calls must never run the user-visible
        // prompt simultaneously (FR-014 spirit). We assert that by routing
        // both through the test hook and observing the in-flight count.
        var prompt = new AvaloniaConfirmationPrompt();
        var inFlight = 0;
        var maxObserved = 0;
        prompt.OverrideForTests(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            maxObserved = Math.Max(maxObserved, now);
            await Task.Delay(20, ct).ConfigureAwait(false);
            Interlocked.Decrement(ref inFlight);
            return true;
        });

        var a = SampleAction(DangerousActionKind.DeleteFile, "/a");
        var b = SampleAction(DangerousActionKind.DeleteFile, "/b");

        var t1 = Task.Run(() => prompt.ConfirmAsync(a, CancellationToken.None));
        var t2 = Task.Run(() => prompt.ConfirmAsync(b, CancellationToken.None));
        Task.WaitAll(new Task[] { t1, t2 }, TimeSpan.FromSeconds(2)).Should().BeTrue();
        maxObserved.Should().Be(1, "the gate must serialise prompts");
        prompt.Dispose();
    }

    private static (ConfirmationDialog dialog, Window owner) OpenDialog(DangerousAction action)
    {
        var owner = new Window { Width = 600, Height = 400 };
        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var dialog = new ConfirmationDialog();
        dialog.Bind(action);
        // ShowDialog requires a parent; we don't await its result here because
        // the test simulates the user input directly and inspects Result.
        _ = dialog.ShowDialog(owner);
        Dispatcher.UIThread.RunJobs();
        return (dialog, owner);
    }

    private static DangerousAction SampleAction(DangerousActionKind kind, string target) =>
        new(
            PolicyDecisionId.New(),
            kind,
            target,
            new PolicyOrigin.FromSkill(new ModuleId("test-module"), new SkillId("test-skill")),
            DateTimeOffset.UtcNow);
}
