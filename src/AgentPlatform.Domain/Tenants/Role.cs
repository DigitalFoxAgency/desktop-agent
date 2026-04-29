namespace AgentPlatform.Domain.Tenants;

public enum Role
{
    Admin = 0,
    Marketer = 1,
    Strategist = 2,
    Designer = 3,
    Engineer = 4,
    MediaBuyer = 5,
}

public static class RoleNames
{
    public const string Admin = "admin";
    public const string Marketer = "marketer";
    public const string Strategist = "strategist";
    public const string Designer = "designer";
    public const string Engineer = "engineer";
    public const string MediaBuyer = "media-buyer";

    public static string ToSlug(Role role) => role switch
    {
        Role.Admin => Admin,
        Role.Marketer => Marketer,
        Role.Strategist => Strategist,
        Role.Designer => Designer,
        Role.Engineer => Engineer,
        Role.MediaBuyer => MediaBuyer,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static Role FromSlug(string slug) => slug switch
    {
        Admin => Role.Admin,
        Marketer => Role.Marketer,
        Strategist => Role.Strategist,
        Designer => Role.Designer,
        Engineer => Role.Engineer,
        MediaBuyer => Role.MediaBuyer,
        _ => throw new ArgumentException($"Unknown role slug: {slug}", nameof(slug)),
    };
}
