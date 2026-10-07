using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Marrow.App.Views.Composer;

public readonly record struct StrikeEntry(int K, double T0, int Windows, int Index, double From, double To, double U);
