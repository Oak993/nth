namespace Marrow.UI.Tests.Fixtures;

// How ShaderLightRenderTests draws the chat text block: composed with a surface under it, Screen over the window's pixels, or the
// CPU fallback's kept still (ED-NEW-lightv2-5).
public enum ShaderLightRoute { Compose, Screen, Still }
