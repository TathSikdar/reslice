namespace InterviewTrea.Core;

/// <summary>
/// RQ-1's text, in one place. Every surface that has to carry it - the banner, the CSV
/// export, the burned-in PNG caption - reads it from here rather than repeating it.
/// </summary>
/// <remarks>
/// In Core because it is not a presentation string: it is a statement about what this
/// software is, and the layer that has no dependencies is the one layer everything else
/// can reach.
/// </remarks>
public static class Disclaimer
{
    /// <summary>
    /// The banner text RQ-1 specifies, character for character, em dash included.
    /// </summary>
    public const string Text =
        "RESEARCH AND DEMONSTRATION USE ONLY — NOT A MEDICAL DEVICE. NOT FOR DIAGNOSTIC USE.";

    /// <summary>
    /// The same sentence with the em dash flattened, for files that are read by machines
    /// rather than by people.
    /// </summary>
    /// <remarks>
    /// A CSV written as UTF-8 without a byte-order mark and opened in a spreadsheet set to
    /// a legacy code page renders the em dash as two mojibake characters on the one line of
    /// the file whose whole purpose is to be read. Derived from <see cref="Text"/> rather
    /// than written out again, so the two cannot drift apart.
    /// </remarks>
    public static readonly string Ascii = Text.Replace('—', '-');
}
