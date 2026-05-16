using System.Collections.Generic;
using System.ComponentModel;

namespace PixelAssetGenerator.Services.Localization
{
    /// <summary>
    /// Service interface for localization (language switching and string lookup).
    /// </summary>
    public interface ILocalizationService : INotifyPropertyChanged
    {
        /// <summary>Current culture code (e.g. "zh-Hans", "en").</summary>
        string CurrentCulture { get; }

        /// <summary>Display name of the current culture.</summary>
        string CurrentCultureDisplayName { get; }

        /// <summary>Indexer for XAML binding: {Binding Strings[Key]}.</summary>
        string this[string key] { get; }

        /// <summary>All available keys for XAML binding discovery.</summary>
        IReadOnlyList<string> AllKeys { get; }

        /// <summary>Fired after language switch for code-side consumers.</summary>
        event Action? CultureChanged;

        /// <summary>Returns the localized string for the given key.</summary>
        string GetString(string key);

        /// <summary>Returns the localized string using cached dictionaries (faster).</summary>
        string GetStringFast(string key);

        /// <summary>Changes the current language and notifies all bindings.</summary>
        void SetCulture(string cultureCode);

        /// <summary>Returns available cultures as (code, displayName) pairs.</summary>
        IReadOnlyList<(string Code, string DisplayName)> GetAvailableCultures();
    }
}
