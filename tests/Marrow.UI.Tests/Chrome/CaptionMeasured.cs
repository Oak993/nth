using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;
using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

namespace Marrow.UI.Tests.Chrome;

// OD-0068 / ED-244: a helper of CaptionContrastTests (one type per file).
internal sealed record CaptionMeasured(double Rest, double Hover, double Pressed, double Inactive, double Ring, double CloseRing);
