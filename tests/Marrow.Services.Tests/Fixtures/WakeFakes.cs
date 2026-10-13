using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-10): the Task Scheduler as a list of names and their XML. It can be told to refuse.</summary>
public sealed class FakeTaskSchedulerHost : ITaskSchedulerHost
{
    public Dictionary<string, string> Tasks { get; } = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public Func<string, string?>? RefuseRegister { get; set; }

    public string? RefuseList { get; set; }

    public string? RefuseDelete { get; set; }

    public Task<IReadOnlyList<string>> List(CancellationToken cancellationToken)
    {
        Calls.Add("list");
        if (RefuseList is { } why) throw new WakeEntryException(why);
        return Task.FromResult<IReadOnlyList<string>>([.. Tasks.Keys]);
    }

    public Task Register(string name, string xml, CancellationToken cancellationToken)
    {
        Calls.Add("register " + name);
        if (RefuseRegister?.Invoke(name) is { } why) throw new WakeEntryException(why);
        Tasks[name] = xml;
        return Task.CompletedTask;
    }

    public Task Delete(string name, CancellationToken cancellationToken)
    {
        Calls.Add("delete " + name);
        if (RefuseDelete is { } why) throw new WakeEntryException(why);
        Tasks.Remove(name);
        return Task.CompletedTask;
    }
}

/// <summary>PORT-ONLY (AS-10): a command-line program that answers from a function and remembers what it was asked, and what a file it was given held at that moment.</summary>
public sealed class FakeProcessRunner(Func<string, IReadOnlyList<string>, ProcessOutcome> answer) : IProcessRunner
{
    public List<(string File, IReadOnlyList<string> Arguments)> Runs { get; } = [];

    /// <summary>The bytes of the file named after /XML at the moment schtasks was run.</summary>
    public byte[]? XmlFile { get; private set; }

    public Exception? Throw { get; set; }

    public Task<ProcessOutcome> Run(string file, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Runs.Add((file, arguments));
        if (Throw is { } failure) throw failure;
        var at = arguments.ToList().IndexOf("/XML");
        if (at >= 0 && at + 1 < arguments.Count && File.Exists(arguments[at + 1])) XmlFile = File.ReadAllBytes(arguments[at + 1]);
        return Task.FromResult(answer(file, arguments));
    }
}

/// <summary>PORT-ONLY (AS-10): the Run key as a dictionary.</summary>
public sealed class FakeRunKey : IRunKey
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? Throw { get; set; }

    public string? Get(string name) => Values.GetValueOrDefault(name);

    public void Set(string name, string command)
    {
        if (Throw is { } failure) throw failure;
        Values[name] = command;
    }

    public void Delete(string name)
    {
        if (Throw is { } failure) throw failure;
        Values.Remove(name);
    }
}
