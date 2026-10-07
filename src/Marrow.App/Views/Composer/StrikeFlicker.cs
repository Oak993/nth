namespace Marrow.App.Views.Composer;

// DEVIATION OD-0084: the locked engine's Mix plan. SPEC K-018..K-040, K-057, K-067.
public readonly record struct StrikeFlicker(double At, double Decay, double Gain);
