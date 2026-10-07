using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Marrow.App.Views.Composer;

public readonly record struct StrikeWindow(double From, double To);
