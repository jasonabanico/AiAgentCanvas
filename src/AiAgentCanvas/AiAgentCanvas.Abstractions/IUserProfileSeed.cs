namespace AiAgentCanvas.Abstractions;

public interface IUserProfileSeed
{
    string Name { get; }
    string Role { get; }
    string? Timezone { get; }
    string Content { get; }
}

public sealed class UserProfileSeed : IUserProfileSeed
{
    public string Name { get; }
    public string Role { get; }
    public string? Timezone { get; }
    public string Content { get; }

    public UserProfileSeed(string name, string role, string? timezone, string content)
    {
        Name = name;
        Role = role;
        Timezone = timezone;
        Content = content;
    }
}
