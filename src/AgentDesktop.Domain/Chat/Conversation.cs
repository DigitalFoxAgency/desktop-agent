namespace AgentDesktop.Domain.Chat;

/// <summary>
/// Persisted thread of messages between a user and the agent.
/// Append-only sequence; messages are never mutated in place.
/// </summary>
public sealed class Conversation
{
    public const int MaxTitleLength = 120;

    private readonly List<Message> _messages;

    public Conversation(
        ConversationId id,
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset lastActivityAt,
        IEnumerable<Message>? messages = null)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (title.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Title cannot exceed {MaxTitleLength} characters.", nameof(title));
        }

        if (lastActivityAt < createdAt)
        {
            throw new ArgumentException(
                "LastActivityAt cannot precede CreatedAt.",
                nameof(lastActivityAt));
        }

        Id = id;
        Title = title;
        CreatedAt = createdAt;
        LastActivityAt = lastActivityAt;
        _messages = messages?.ToList() ?? new List<Message>();
        ValidateMessageInvariants();
    }

    public ConversationId Id { get; }
    public string Title { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastActivityAt { get; private set; }
    public IReadOnlyList<Message> Messages => _messages;

    /// <summary>Append a new message. Updates <see cref="LastActivityAt"/>.</summary>
    public void Append(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ConversationId != Id)
        {
            throw new ArgumentException(
                $"Message belongs to conversation {message.ConversationId}, not {Id}.",
                nameof(message));
        }

        var expectedIndex = _messages.Count;
        if (message.Index != expectedIndex)
        {
            throw new ArgumentException(
                $"Expected message index {expectedIndex} but got {message.Index}.",
                nameof(message));
        }

        if (message.CreatedAt < LastActivityAt)
        {
            throw new ArgumentException(
                "Message CreatedAt must be ≥ LastActivityAt.",
                nameof(message));
        }

        _messages.Add(message);
        LastActivityAt = message.CreatedAt;
    }

    /// <summary>
    /// Set the conversation title (e.g. derived from the first user
    /// message or chosen by the user). Idempotent and validating.
    /// </summary>
    public void Rename(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title cannot be empty or whitespace.", nameof(title));
        }

        if (title.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Title cannot exceed {MaxTitleLength} characters.", nameof(title));
        }

        Title = title;
    }

    private void ValidateMessageInvariants()
    {
        for (var i = 0; i < _messages.Count; i++)
        {
            var msg = _messages[i];
            if (msg.Index != i)
            {
                throw new ArgumentException(
                    $"Message at position {i} has index {msg.Index}; conversation messages must be 0-based and contiguous.",
                    nameof(_messages));
            }

            if (msg.ConversationId != Id)
            {
                throw new ArgumentException(
                    $"Message {msg.Id} belongs to conversation {msg.ConversationId}, not {Id}.",
                    nameof(_messages));
            }
        }
    }
}
