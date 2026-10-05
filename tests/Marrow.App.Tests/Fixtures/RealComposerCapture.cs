namespace Marrow.App.Tests.Fixtures;

/// <summary>What a real composer (ComposerView at 472 wide) drew for the parts the Customization preview restates: the ring button, the chip's chevron end, the send disc at rest and with a draft, and the words of its placeholder (CZ-T45).</summary>
internal sealed record RealComposerCapture(byte[] Ring, (int W, int H) RingSize, byte[] ChevronEnd, (int W, int H) ChevronEndSize, double ChevronGap, byte[] DiscRest, byte[] DiscTyping, (int W, int H) DiscSize, string Words);
