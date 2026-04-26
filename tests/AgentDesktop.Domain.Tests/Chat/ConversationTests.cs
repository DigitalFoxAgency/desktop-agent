using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Domain.Tests.Chat;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_conversation_can_be_created()
    {
        var id = ConversationId.New();
        var convo = new Conversation(id, "draft", T0, T0);

        convo.Id.Should().Be(id);
        convo.Title.Should().Be("draft");
        convo.Messages.Should().BeEmpty();
        convo.CreatedAt.Should().Be(T0);
        convo.LastActivityAt.Should().Be(T0);
    }

    [Fact]
    public void LastActivityAt_must_be_at_or_after_CreatedAt()
    {
        var act = () => new Conversation(ConversationId.New(), "title", T0, T0.AddSeconds(-1));
        act.Should().Throw<ArgumentException>().WithMessage("*LastActivityAt*");
    }

    [Fact]
    public void Append_increments_index_monotonically()
    {
        var convo = NewConversation();
        var m0 = NewUserMessage(convo.Id, 0, "hi", T0.AddSeconds(1));
        var m1 = NewUserMessage(convo.Id, 1, "again", T0.AddSeconds(2));

        convo.Append(m0);
        convo.Append(m1);

        convo.Messages.Should().HaveCount(2);
        convo.Messages[0].Index.Should().Be(0);
        convo.Messages[1].Index.Should().Be(1);
        convo.LastActivityAt.Should().Be(T0.AddSeconds(2));
    }

    [Fact]
    public void Append_rejects_wrong_conversation_id()
    {
        var convo = NewConversation();
        var foreign = NewUserMessage(ConversationId.New(), 0, "hi", T0.AddSeconds(1));

        var act = () => convo.Append(foreign);
        act.Should().Throw<ArgumentException>().WithMessage("*conversation*");
    }

    [Fact]
    public void Append_rejects_non_contiguous_index()
    {
        var convo = NewConversation();
        var skip = NewUserMessage(convo.Id, 1, "hi", T0.AddSeconds(1));

        var act = () => convo.Append(skip);
        act.Should().Throw<ArgumentException>().WithMessage("*index*");
    }

    [Fact]
    public void Append_rejects_message_older_than_LastActivityAt()
    {
        var convo = NewConversation();
        convo.Append(NewUserMessage(convo.Id, 0, "first", T0.AddSeconds(10)));

        var act = () => convo.Append(NewUserMessage(convo.Id, 1, "back-in-time", T0.AddSeconds(5)));
        act.Should().Throw<ArgumentException>().WithMessage("*CreatedAt*");
    }

    [Fact]
    public void Message_originating_delegation_and_skill_are_mutually_exclusive()
    {
        var act = () => new Message(
            id: MessageId.New(),
            conversationId: ConversationId.New(),
            index: 0,
            author: MessageAuthor.Agent,
            body: "hi",
            createdAt: T0,
            originatingDelegation: new DelegationRef(new ModuleId("m"), "onboard-client"),
            originatingSkill: new SkillRef(new ModuleId("m"), new SkillId("k")));

        act.Should().Throw<ArgumentException>().WithMessage("*delegation*skill*");
    }

    [Fact]
    public void Rename_validates_title()
    {
        var convo = NewConversation();
        var act = () => convo.Rename(string.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*empty*");
    }

    [Fact]
    public void Rename_rejects_too_long_title()
    {
        var convo = NewConversation();
        var act = () => convo.Rename(new string('x', Conversation.MaxTitleLength + 1));
        act.Should().Throw<ArgumentException>().WithMessage("*characters*");
    }

    private static Conversation NewConversation() =>
        new(ConversationId.New(), "draft", T0, T0);

    private static Message NewUserMessage(ConversationId convo, int index, string body, DateTimeOffset at) =>
        new(MessageId.New(), convo, index, MessageAuthor.User, body, at);
}
