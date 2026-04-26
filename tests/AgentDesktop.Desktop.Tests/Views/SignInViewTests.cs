using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Desktop.ViewModels;
using AgentDesktop.Desktop.Views;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AgentDesktop.Desktop.Tests.Views;

/// <summary>
/// Headless a11y + interaction coverage for <see cref="SignInView"/> (T061).
/// Asserts keyboard-only flow: TokenInput receives initial focus when
/// requested, Tab moves to Submit, Enter triggers the submit command,
/// and label association + automation names are wired correctly.
/// </summary>
public sealed class SignInViewTests
{
    [AvaloniaFact]
    public void View_renders_token_input_with_label_and_automation_name()
    {
        var (view, _) = BuildView();
        ShowInWindow(view);

        var tokenLabel = view.FindControl<TextBlock>("TokenLabel");
        var tokenInput = view.FindControl<TextBox>("TokenInput");
        var submit = view.FindControl<Button>("SubmitButton");

        tokenLabel.Should().NotBeNull("the visible label is required for screen-reader association");
        tokenInput.Should().NotBeNull();
        submit.Should().NotBeNull();

        AutomationProperties.GetName(tokenInput!).Should().Be("Subscription session token");
        AutomationProperties.GetName(submit!).Should().Be("Sign in");
        AutomationProperties.GetLabeledBy(tokenInput!).Should().BeSameAs(tokenLabel);
    }

    [AvaloniaFact]
    public void Keyboard_only_flow_signs_in_via_enter_key()
    {
        var (view, vm) = BuildView();
        var window = ShowInWindow(view);

        var tokenInput = view.FindControl<TextBox>("TokenInput")!;
        var submit = view.FindControl<Button>("SubmitButton")!;

        tokenInput.Focus();
        tokenInput.IsFocused.Should().BeTrue("token input must receive focus by default");

        // Type a token via the headless TextInput channel.
        window.KeyTextInput("test-token");
        Dispatcher.UIThread.RunJobs();
        vm.SessionToken.Should().Be("test-token");

        // Tab should move focus to the Submit button (focus order check).
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        submit.IsFocused.Should().BeTrue("Tab must move focus from the token input to the Sign in button");

        // Enter triggers the default button regardless of focus.
        var signedIn = false;
        vm.SignedIn += (_, _) => signedIn = true;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // The submit command awaits the validator; pump until it completes.
        WaitForSignIn(vm, () => signedIn);
        signedIn.Should().BeTrue("submit command must raise SignedIn after the gate accepts the token");
    }

    [AvaloniaFact]
    public void Empty_token_keeps_submit_disabled()
    {
        var (view, _) = BuildView();
        ShowInWindow(view);
        var submit = view.FindControl<Button>("SubmitButton")!;
        submit.IsEffectivelyEnabled.Should().BeFalse(
            "Sign in must remain disabled until a non-empty token is supplied");
    }

    private static (SignInView view, SignInViewModel vm) BuildView()
    {
        var gate = new FakeSubscriptionGate();
        var vm = new SignInViewModel(gate);
        var view = new SignInView { DataContext = vm };
        return (view, vm);
    }

    private static Window ShowInWindow(SignInView view)
    {
        var window = new Window { Width = 600, Height = 480, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void WaitForSignIn(SignInViewModel vm, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
