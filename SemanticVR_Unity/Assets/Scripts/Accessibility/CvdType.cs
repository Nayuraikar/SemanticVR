namespace SemanticVR.Accessibility
{
    /// <summary>Colour-vision profile types. "-opia" types are full dichromacy; "-omaly" types use the severity slider.</summary>
    public enum CvdType
    {
        Normal = 0,
        Protanopia, Protanomaly,
        Deuteranopia, Deuteranomaly,
        Tritanopia, Tritanomaly,
        Achromatopsia,
        Custom
    }

    /// <summary>Baseline correction modes (presets of the baseline method; see BaselineCorrection).</summary>
    public enum CorrectionMode { Balanced = 0, MaxDistinguishability, NaturalAppearance, HighContrast, Custom }
}
