namespace Apptrack.Entities;

public sealed class User
{
    private User() { }

    public User(Guid id, string email, string provider, DateTime createdAtUtc)
    {
        Id = id;
        Email = email;
        Provider = provider;
        CreatedAt = createdAtUtc;
    }

    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public DateTime CreatedAt { get; private set;  }
    public DateTime? DeletedAt { get; set; }

    public void Delete(DateTime utcNow) => DeletedAt ??= utcNow;
    public void Restore() => DeletedAt = null;
}