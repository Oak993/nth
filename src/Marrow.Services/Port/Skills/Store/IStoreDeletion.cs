// PORT-ONLY: SK-5. Real file deletion; a test may inject Windows sharing failures on POSIX.
namespace Marrow.Services.Port.Skills.Store;

public interface IStoreDeletion
{
    void Delete(string folder);
}
