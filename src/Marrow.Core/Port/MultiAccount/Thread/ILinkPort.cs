// PORT-ONLY: multi-account steps 3b-5, job 0 (OD-0115 link=A). Link up: a linked account checks each answer of the thread. The engine is a later job (E1); the popover binds this.
namespace Marrow.Core.Port.MultiAccount.Thread;

public interface ILinkPort
{
    /// <summary>Raised with the thread whose links changed.</summary>
    event Action<Guid>? Changed;

    /// <summary>The accounts linked to the thread, in the order they were linked.</summary>
    IReadOnlyList<Guid> Linked(Guid thread);

    void Link(Guid thread, Guid account);

    void Unlink(Guid thread, Guid account);
}
