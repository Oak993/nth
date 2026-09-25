using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Port;

namespace Marrow.App.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraButton.swift (B2 1fbf81e), :1-711.
// Skeleton (c100 group 2): every type the file declares, the views' inputs and the members the other cycle files use.
// Every view body and every member with behaviour is left to the implementing unit (MARROW_STUB), as are HydraButton's
// private members (:106-197), HydraRightTapHost's monitor (:254-293) and HydraChargeLayerView's layers, lifecycle and
// animations (:422-612).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; an NSView is a Control; an
// NSViewRepresentable is the Decorator that hosts its NSView; Color and NSColor are SwiftColor; a caseless enum used
// as a namespace is a static class; `@Binding var x` is a Binding<T> (R-VIEW-2). Swift `private` top-level types are
// `internal` here: C# has no private top-level type, and `file` cannot appear in another type's member signatures.

/// <summary>:4-198 <c>struct HydraButton: View</c>: the chat's Hydra mark, first in the chrome row.</summary>
public sealed class HydraButton : Decorator
{
    // :12-13 the view's inputs; :26-101 body
    public HydraButton(ChatThread thread, ThreadRuntime runtime) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:26");
}

/// <summary>:200-230 <c>private struct HydraSwitchPopover: View</c>.</summary>
internal sealed class HydraSwitchPopover : Decorator
{
    // :204-206 the view's inputs; :208-229 body
    public HydraSwitchPopover(ChatThread thread, bool isOn, Binding<bool> isHydraSwitchShown) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:208");
}

/// <summary>:232-250 <c>private struct HydraRightTap: NSViewRepresentable</c>: hosts its <see cref="HydraRightTapHost"/>.</summary>
internal sealed class HydraRightTap : Decorator
{
    // :241 var action; :243-249 makeNSView, updateNSView
    public HydraRightTap(Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:243");
}

/// <summary>:252-294 <c>private final class HydraRightTapHost: NSView</c>.</summary>
internal sealed class HydraRightTapHost : Control
{
    // :253 var action: () -> Void
    public Action Action { get; set; }

    // :256-259 init(action:)
    public HydraRightTapHost(Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:256");
}

/// <summary>:296-319 <c>private struct HydraIdlePopover: View</c>.</summary>
internal sealed class HydraIdlePopover : Decorator
{
    // :301 let thread; :303-318 body
    public HydraIdlePopover(ChatThread thread) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:303");
}

/// <summary>:321-331 <c>private struct HydraMarkView: View</c>.</summary>
internal sealed class HydraMarkView : Decorator
{
    // :323-324 the view's inputs; :326-330 body
    public HydraMarkView(bool isOn, bool isHovering) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:326");
}

/// <summary>:333-387 <c>struct HydraCharge: View</c>: the charge around an active Hydra button.</summary>
public sealed class HydraCharge : Decorator
{
    // :344 fileprivate static let colors: [Color]
    internal static ValueList<SwiftColor> Colors =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:344");

    // :346 fileprivate static let layerColors: [NSColor]
    internal static ValueList<SwiftColor> LayerColors =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:346");

    // :342 var isWorking = true; :348-358 body
    public HydraCharge(bool isWorking = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:348");

    // :360-372 static func glow(breathing:) -> some View
    public static Control Glow(bool breathing) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:360");

    // :374-386 static func ring(turned:bright:) -> some View
    public static Control Ring(bool turned, bool bright = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:374");
}

/// <summary>:389-411 <c>private struct HydraChargeMotion: NSViewRepresentable</c>: hosts a
/// <see cref="HydraChargeLayerView"/>.</summary>
internal sealed class HydraChargeMotion : Decorator
{
    // :397-410 makeNSView, updateNSView, sizeThatFits
    public HydraChargeMotion() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:397");
}

/// <summary>:413-613 <c>final class HydraChargeLayerView: NSView</c>: the moving charge as layers.</summary>
public sealed class HydraChargeLayerView : Control
{
    private const string TurnKey = "turn"; // :428
    private const string BreathKey = "breathe"; // :429
    private const float RingRestOpacity = 0.85f; // :431
    private const float GlowRestOpacity = 0.6f; // :432
    private const double TurnDuration = 4; // :433
    private const double BreathDuration = 1.4; // :434
    private const double LineWidth = 2; // :435
    private const double GlowStartRadius = 2; // :437
    private const double GlowEndRadius = 15; // :438

    // :440-480 override init(frame:)
    public HydraChargeLayerView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:440");

    // :493-501 func configure(colors:accent:)
    public void Configure(IReadOnlyList<SwiftColor> colors, SwiftColor accent) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:493");
}

/// <summary>:615-646 <c>private struct HydraSendWorkingBadge: View</c>.</summary>
internal sealed class HydraSendWorkingBadge : Decorator
{
    // :621-622 the view's inputs; :624-645 body
    public HydraSendWorkingBadge(long running, bool isSending) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:624");
}

/// <summary>:648-711 <c>enum HydraPairSummary</c>, a caseless enum used as a namespace: one-line summaries of a pair.</summary>
public static class HydraPairSummary
{
    // :652-657 @MainActor static func workerModel(_:registry:) -> String?
    public static string? WorkerModel(HydraPair pair, ProviderRegistry registry)
    {
        if (pair.WorkerModel is { } model) return registry.Model(model, pair.HeadsProvider)?.ShortName ?? model;
        if (!pair.SendsHeadsElsewhere) return null;
        return registry.DefaultModel(pair.HeadsProvider)?.ShortName;
    }

    // :661-673 @MainActor static func workers(_:registry:) -> String
    public static string Workers(HydraPair pair, ProviderRegistry registry) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:661");

    // :676-678 static func providers(_:) -> String
    public static string Providers(HydraPair pair) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraButton.swift:676");

    // :683-693 static func efforts(_:) -> String?
    public static string? Efforts(HydraPair pair)
    {
        var lead = pair.OrchestratorEffort is { } leadEffort ? SwiftString.Lowercased(ModelOption.EffortTitle(leadEffort)) : null;
        var heads = pair.WorkerEffort is { } headsEffort ? SwiftString.Lowercased(ModelOption.EffortTitle(headsEffort)) : null;
        if (lead is not null && heads is not null && lead == heads) return Capitalized(lead) + " effort";
        if (lead is not null && heads is not null) return "Lead " + lead + ", heads " + heads + " effort";
        if (lead is not null) return "Lead " + lead + " effort";
        if (heads is not null) return "Heads " + heads + " effort";
        return null;
    }

    // Swift `.capitalized`: the first letter of every word upper-cased, the rest untouched (R-STR-4).
    private static string Capitalized(string text) =>
        string.Join(" ", text.Split(' ').Select(word =>
            word.Length == 0 ? word : SwiftString.Uppercased(SwiftString.Prefix(word, 1)) + SwiftString.DropFirst(word, 1)));

    // :697-704 @MainActor static func modelsTitle(_:registry:) -> String
    public static string ModelsTitle(HydraPair pair, ProviderRegistry registry)
    {
        var lead = pair.OrchestratorModel is { } orchestratorModel
            ? registry.Model(orchestratorModel, pair.Provider)?.ShortName ?? orchestratorModel
            : "Any " + pair.Provider.DisplayName + " model";
        var worker = WorkerModel(pair, registry) ?? (pair.SendsHeadsElsewhere ? pair.HeadsProvider.DisplayName : null);
        return worker is null ? lead + " + itself" : lead + " + " + worker;
    }

    // :707-710 @MainActor static func title(_:registry:) -> String
    public static string Title(HydraPair pair, ProviderRegistry registry) =>
        pair.CustomName ?? ModelsTitle(pair, registry);
}
