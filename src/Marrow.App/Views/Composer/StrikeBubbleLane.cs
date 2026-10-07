using System.Globalization;

namespace Marrow.App.Views.Composer;

internal readonly record struct StrikeBubbleLane(int K, StrikeBubbleRise Rise, double Start, bool Ready);
