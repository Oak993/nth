using System.Reflection;
using System.Runtime.ExceptionServices;
using SkiaSharp;

namespace Marrow.App.Tests.Wallpapers;

// WL-1 publishes these names/signatures on origin/codex/f-wl-1. Public reflection lets the test project
// compile on staging before WL-1 lands; it never reaches private shader strings, uniforms or caches.
// WL-3 adds public Dynamic/Persp/Focus selection inputs and a WallpaperEnvironment constructor input.
// Missing public APIs or premium data fail the gate; there are no discovery-time skips.
public static class WallpaperRendererBridge
{
    private const string Prefix = "Marrow.UI.Theme.Wallpapers.";
    private static Type? Find(string name) => Assembly.Load("Marrow.UI").GetType(Prefix + name);
    private static ConstructorInfo? SelectionConstructor => Find("WallpaperSelection")?.GetConstructors()
        .Where(c => new[] { "kind", "id", "pack", "speed", "options" }.All(n => c.GetParameters().Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))))
        .OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
    private static ConstructorInfo? RendererConstructor => Find("WallpaperRenderer")?.GetConstructors().FirstOrDefault(c =>
        c.GetParameters() is var p && p.Length is 2 or 3 && p[0].ParameterType == Find("WallpaperSelection")
        && p[1].ParameterType == typeof(bool) && (p.Length == 2 || p[2].ParameterType == Find("WallpaperEnvironment")));
    private static ConstructorInfo? EnvironmentConstructor => Find("WallpaperEnvironment")?.GetConstructor(new[] {
        typeof(TimeProvider), Find("IWallpaperAccentSource") ?? typeof(void), Find("IWallpaperWindowPosition") ?? typeof(void) });
    private static MethodInfo? DrawFrame => Find("WallpaperRenderer")?.GetMethod("DrawFrameAt", new[] { typeof(SKCanvas), typeof(int), typeof(int), typeof(double) });

    public static string? SkipReason(string collection, bool premium)
    {
        var renderer = Find("WallpaperRenderer"); var selection = Find("WallpaperSelection"); var kind = Find("WallpaperKind");
        if (renderer is null || selection is null || kind is null || SelectionConstructor is null || DrawFrame is null || RendererConstructor is null)
            return "WL-1 has not landed: public WallpaperRenderer selection constructor and DrawFrameAt(SKCanvas, int, int, double) are required (origin/codex/f-wl-1).";
        var parameters = SelectionConstructor.GetParameters();
        if (RendererConstructor.GetParameters().Length == 3 && EnvironmentConstructor is null)
            return "WL-3/WL-4 public WallpaperEnvironment(TimeProvider, IWallpaperAccentSource, IWallpaperWindowPosition) is missing.";
        if (premium)
        {
            var missing = new[] { "dynamic", "focus" }
                .Where(n => !parameters.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length != 0 || EnvironmentConstructor is null)
                return $"{(string.Equals(collection, "animated", StringComparison.Ordinal) ? "WL-3" : "WL-4")} has not landed: public Dynamic/Focus selection inputs and WallpaperEnvironment for fixed clock/accent are required. Missing inputs: {string.Join(", ", missing)}.";
            if (string.Equals(collection, "static", StringComparison.Ordinal))
            {
                var get = Find("WallpaperCatalogue")?.GetMethod("Get", new[] { kind, typeof(string) });
                if (get is not null)
                {
                    var definition = Invoke(() => get.Invoke(null, new[] { Enum.Parse(kind, "Static"), "riptide" }));
                    var available = new[] { "Premium", "StaticPremium", "Static" }.Any(name => definition.GetType().GetProperty(name)?.GetValue(definition) is not null);
                    if (!available) return "WL-4 has not landed: static WallpaperDefinition premium data is missing; WL-3 only supplies animated premium data.";
                }
            }
        }
        var supported = new[] { "kind", "id", "pack", "speed", "options", "dynamic", "clockhours", "accent", "focus", "persp", "perspective" };
        var required = parameters.Where(p => !p.HasDefaultValue && !supported.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).Select(p => p.Name).ToArray();
        return required.Length == 0 ? null : "WL-1/WL-3/WL-4 public selection API needs binding for required inputs: " + string.Join(", ", required);
    }

    public static SKBitmap Render(WallpaperFidelityCase reference)
    {
        var reason = SkipReason(reference.Collection, reference.Premium);
        if (reason is not null) throw new InvalidOperationException(reason); // an invalid gate never passes a render
        var inputs = reference.Inputs;
        var kind = Enum.Parse(Find("WallpaperKind")!, string.Equals(reference.Collection, "static", StringComparison.Ordinal) ? "Static" : "Animated");
        var catalogueGet = Find("WallpaperCatalogue")?.GetMethod("Get", new[] { Find("WallpaperKind")!, typeof(string) });
        if (catalogueGet is not null)
        {
            try { Invoke(() => catalogueGet.Invoke(null, new[] { kind, reference.Wallpaper })); }
            catch (InvalidOperationException e)
            { throw new InvalidOperationException($"{(string.Equals(reference.Collection, "animated", StringComparison.Ordinal) ? "WL-3" : "WL-4")} catalogue is missing approved wallpaper {reference.Wallpaper}", e); }
        }
        var options = inputs.GetProperty("options").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.Ordinal);
        object? Value(ParameterInfo p) => p.Name?.ToLowerInvariant() switch
        {
            "kind" => kind,
            "id" => reference.Wallpaper,
            "pack" => reference.Collection == "static" && reference.Variant == "match-accent"
                ? "accent" : inputs.GetProperty("packId").GetString(),
            "speed" => inputs.GetProperty("speed").GetDouble(),
            "options" => options,
            "dynamic" => inputs.GetProperty("dynamic").GetBoolean(),
            "clockhours" => inputs.GetProperty("clockHours").GetDouble(),
            "accent" => inputs.GetProperty("accent").GetString(),
            "focus" => inputs.GetProperty("focus").GetDouble(),
            "persp" or "perspective" => inputs.GetProperty("perspective").GetBoolean(),
            _ => p.DefaultValue,
        };
        var constructor = SelectionConstructor!;
        var selection = Invoke(() => constructor.Invoke(constructor.GetParameters().Select(Value).ToArray()));
        var rendererConstructor = RendererConstructor!;
        var arguments = new List<object?> { selection, inputs.GetProperty("dark").GetBoolean() };
        if (rendererConstructor.GetParameters().Length == 3)
        {
            var accent = DispatchProxy.Create(Find("IWallpaperAccentSource")!, typeof(WallpaperFidelityEnvironmentProxy));
            ((WallpaperFidelityEnvironmentProxy)accent).Value = inputs.GetProperty("accent").GetString()!;
            var position = DispatchProxy.Create(Find("IWallpaperWindowPosition")!, typeof(WallpaperFidelityEnvironmentProxy));
            ((WallpaperFidelityEnvironmentProxy)position).Value = (inputs.GetProperty("tilt")[0].GetDouble(), inputs.GetProperty("tilt")[1].GetDouble());
            arguments.Add(Invoke(() => EnvironmentConstructor!.Invoke(new object[] { new WallpaperFidelityClock(inputs.GetProperty("clockHours").GetDouble()), accent, position })));
        }
        using var renderer = (IDisposable)Invoke(() => rendererConstructor.Invoke(arguments.ToArray()));
        // Settle the public followers: a fixed oracle frame represents a settled selection and pointer.
        var settle = Find("WallpaperRenderer")!.GetMethod("Settle", Type.EmptyTypes)
            ?? throw new InvalidOperationException("WallpaperRenderer.Settle is required for fixed inputs.");
        Invoke(() => settle.Invoke(renderer, null));
        using var surface = SKSurface.Create(new SKImageInfo(160, 100, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Could not create Skia raster surface");
        surface.Canvas.Clear(SKColors.Transparent);
        Invoke(() => DrawFrame!.Invoke(renderer, new object[] { surface.Canvas, 160, 100, inputs.GetProperty("time").GetDouble() }));
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static object Invoke(Func<object?> action)
    {
        try { return action()!; }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
}
