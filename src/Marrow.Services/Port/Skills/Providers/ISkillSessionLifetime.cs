// PORT-ONLY: SK-17. A leased generation stays readable until the child has actually exited, rather than only receiving Stop.
namespace Marrow.Services.Port.Skills.Providers;

public interface ISkillSessionLifetime
{
    Task SkillReadersExited { get; }
}
