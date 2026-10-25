using System;
using System.Globalization;

namespace Lobster;

public enum Language { Fr, En }

/// <summary>
/// Two-language UI. Every user-facing string is written as L("français", "English"),
/// so both texts sit side by side in the code and can never drift apart.
/// </summary>
public static class Loc
{
    public static bool IsEnglish => AppState.Settings.Language == Language.En;

    public static string L(string fr, string en) => IsEnglish ? en : fr;

    /// <summary>Default for a new install: French when Windows is in French, English otherwise.</summary>
    public static Language SystemDefault =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? Language.Fr : Language.En;

    /// <summary>"Ko/Mo/Go" in French, "KB/MB/GB" in English.</summary>
    public static string[] SizeUnits => IsEnglish ? new[] { "B", "KB", "MB", "GB" } : new[] { "o", "Ko", "Mo", "Go" };
}
