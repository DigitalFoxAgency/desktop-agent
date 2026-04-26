using AgentDesktop.Application.Chat;
using AgentDesktop.Contracts.Tests.Fakes;
using AgentDesktop.Desktop.ViewModels;
using AgentDesktop.Domain;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentDesktop.Desktop.Tests.EndToEnd;

/// <summary>
/// US1 end-to-end smoke (T074): drive sign-in → start a conversation →
/// send a message → assert the streamed reply renders → drop the
/// process state and rebuild it → assert the conversation persists in
/// history. Uses an in-memory chat repository so the test never
/// touches disk; the runtime is the contract-test
/// <see cref="FakeRuntimeManager"/>.
/// </summary>
public sealed class SignInAndChatTests
{
    [AvaloniaFact]
    public async Task Sign_in_then_send_message_streams_reply_and_persists_history()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
        var subscription = new FakeSubscriptionGate { CurrentStatus = SubscriptionStatus.Unknown, CurrentAccount = null };
        var repo = new FakeChatRepository();
        await using var runtime = new FakeRuntimeManager();
        runtime.TransitionTo(RuntimeStatus.Starting);
        runtime.TransitionTo(RuntimeStatus.Ready);

        var chatService = new ChatService(repo, runtime, subscription, clock, NullLogger<ChatService>.Instance);
        var conversations = new ConversationListViewModel(chatService);
        var chat = new ChatViewModel(chatService);
        var signIn = new SignInViewModel(subscription);
        var shell = new ShellViewModel(subscription, signIn, conversations, chat);

        // Sign in.
        signIn.SessionToken = "demo-token";
        await signIn.SubmitCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        await WaitForAsync(() => shell.IsSignedIn);
        shell.IsSignedIn.Should().BeTrue();
        await WaitForAsync(() => chat.HasConversation);
        chat.HasConversation.Should().BeTrue("HandleSignedIn should have started a conversation");

        // Send a message and observe streamed chunks render into the surface.
        chat.Draft = "hello agent";
        await chat.SendCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        await WaitForAsync(() => !chat.IsStreaming);

        chat.Messages.Should().Contain(m => m.Author == MessageAuthor.User && m.Body == "hello agent");
        chat.Messages.Should().Contain(m =>
            m.Author == MessageAuthor.Agent && m.Body.Contains("hello agent", StringComparison.Ordinal));

        var firstConvoId = chat.ActiveConversationId;
        repo.Calls.Should().Contain(c => c.StartsWith($"Append({firstConvoId}", StringComparison.Ordinal));

        // Simulate an app restart: rebuild the view models against the same
        // repository (its data survives the process). History should reload.
        var conversations2 = new ConversationListViewModel(chatService);
        await conversations2.LoadAsync();
        conversations2.Items.Should().NotBeEmpty();
        conversations2.Items[0].Id.Should().Be(firstConvoId, "history must survive a 'restart'");

        var chat2 = new ChatViewModel(chatService);
        await chat2.OpenAsync(firstConvoId);
        chat2.Messages.Should().Contain(m => m.Author == MessageAuthor.User && m.Body == "hello agent");
        chat2.Messages.Should().Contain(m => m.Author == MessageAuthor.Agent);
    }

    [AvaloniaFact]
    public async Task Subscription_gate_off_blocks_send_with_system_message()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero));
        var subscription = new FakeSubscriptionGate { CurrentStatus = SubscriptionStatus.Expired, CurrentAccount = null };
        var repo = new FakeChatRepository();
        await using var runtime = new FakeRuntimeManager();
        runtime.TransitionTo(RuntimeStatus.Starting);
        runtime.TransitionTo(RuntimeStatus.Ready);

        var chatService = new ChatService(repo, runtime, subscription, clock, NullLogger<ChatService>.Instance);
        var convo = await chatService.StartConversationAsync(CancellationToken.None);
        var chat = new ChatViewModel(chatService);
        await chat.OpenAsync(convo.Id);

        chat.Draft = "should be blocked";
        await chat.SendCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        await WaitForAsync(() => !chat.IsStreaming);

        chat.Messages.Should().Contain(m =>
            m.Author == MessageAuthor.System &&
            m.Body.Contains("Expired", StringComparison.Ordinal));
        runtime.ChatLog.Should().BeEmpty("runtime must never be called when the gate is off");
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
