using Avalonia;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Fixtures;

// The image and screen shapes exercised here come from AttachmentFlight.swift:11-15,60-64.
public sealed class FlightImage : IImage
{
    public Size Size => new(24, 24);
    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) { }
}

public sealed class FlightSurface : IAttachmentFlightSurface
{
    public List<(Rect Frame, double Radius)> States { get; } = [];
    public int Shows { get; private set; }
    public int OrderOuts { get; private set; }
    public void Apply(Rect frame, double radius) => States.Add((frame, radius));
    public void Show() => Shows++;
    public void OrderOut() => OrderOuts++;
}

public sealed class FlightPlatform : IAttachmentFlightPlatform
{
    public bool ReduceMotion { get; set; }
    public bool ImageAvailable { get; set; } = true;
    public Rect? MainScreen { get; set; } = new Rect(0, 0, 1600, 900);
    public List<(Rect Frame, Rect Screen, IImage Image, FlightSurface Surface)> Created { get; } = [];
    public IImage? ResolveImage(IImage image) => ImageAvailable ? image : null;
    public IAttachmentFlightSurface Create(Rect frame, Rect screen, IImage image)
    {
        var surface = new FlightSurface();
        Created.Add((frame, screen, image, surface));
        return surface;
    }
}
