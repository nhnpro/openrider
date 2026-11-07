namespace Tasks.Core;

/// <summary>An in-memory list of tasks.</summary>
public sealed class TaskStore
{
    private readonly List<TaskItem> _items = new();
    private int _nextId = 1;

    public IReadOnlyList<TaskItem> Items => _items;

    public TaskItem Add(string title, Priority priority = Priority.Normal)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("A task needs a title.", nameof(title));
        }
        var item = new TaskItem(_nextId++, title.Trim(), priority);
        _items.Add(item);
        return item;
    }

    public bool Complete(int id)
    {
        var index = _items.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            return false;
        }
        _items[index] = _items[index].Complete();
        return true;
    }

    // TODO: case-insensitive, word-based search instead of a substring match
    public IEnumerable<TaskItem> Search(string text) => _items.Where(t => t.Title.Contains(text));

    public IEnumerable<TaskItem> Pending() =>
        _items.Where(t => !t.Done).OrderByDescending(t => t.Priority).ThenBy(t => t.Id);

    public int CountDone()
    {
        int unused = 0; // QA: intentional warning (CS0219, assigned but never used)
        return _items.Count(t => t.Done);
    }
}
