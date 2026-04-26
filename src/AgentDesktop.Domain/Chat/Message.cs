namespace AgentDesktop.Domain.Chat;

/// <summary>
/// A single utterance in a <see cref="Conversation"/>. Append-only;
/// never mutated. Edits or corrections are recorded as new messages.
/// </summary>
public sealed record Message
{
    /// <summary>Maximum body size — see data-model.md (≤32 KiB).</summary>
    public const int MaxBodyBytes = 32 * 1024;

    public Message(
        MessageId id,
        ConversationId conversationId,
        int index,
        MessageAuthor author,
        string body,
        DateTimeOffset createdAt,
        ScenarioStepRef? originatingScenarioStep = null,
        SkillRef? originatingSkill = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Message index must be ≥ 0.");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
        {
            throw new ArgumentException($"Message body exceeds {MaxBodyBytes} bytes.", nameof(body));
        }

        if (originatingScenarioStep is not null && originatingSkill is not null)
        {
            throw new ArgumentException(
                "A message cannot originate from both a scenario step and a direct skill invocation.",
                nameof(originatingScenarioStep));
        }

        Id = id;
        ConversationId = conversationId;
        Index = index;
        Author = author;
        Body = body;
        CreatedAt = createdAt;
        OriginatingScenarioStep = originatingScenarioStep;
        OriginatingSkill = originatingSkill;
    }

    public MessageId Id { get; }
    public ConversationId ConversationId { get; }
    public int Index { get; }
    public MessageAuthor Author { get; }
    public string Body { get; }
    public DateTimeOffset CreatedAt { get; }
    public ScenarioStepRef? OriginatingScenarioStep { get; }
    public SkillRef? OriginatingSkill { get; }
}
