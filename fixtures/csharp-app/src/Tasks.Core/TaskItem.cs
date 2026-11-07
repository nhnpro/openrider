namespace Tasks.Core;

public enum Priority
{
    Low,
    Normal,
    High,
}

/// <summary>A task in the list.</summary>
public sealed record TaskItem(int Id, string Title, Priority Priority = Priority.Normal, bool Done = false)
{
    public TaskItem Complete() => this with { Done = true };

    public override string ToString() => $"#{Id} [{(Done ? 'x' : ' ')}] {Title} ({Priority})";
}
