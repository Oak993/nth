using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Marrow.App.Views.Composer;

// SPEC K-166/K-169: an injectable uint32 / 2^32 draw source.
public interface IStrikeDraws
{
    double Next();
}
