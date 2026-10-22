using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using Microsoft.Win32;
using System.Windows.Markup;

namespace WindowsGSM.Localization
{
    public static class LocalizationManager
    {
        private const string RegistryPath = @"SOFTWARE\WindowsGSM";
        private const string LanguageSettingName = "Language";
        private static readonly ResourceManager EnglishResources = new ResourceManager("WindowsGSM.Localization.Strings", typeof(LocalizationManager).Assembly);
        private static readonly ResourceManager SpanishResources = new ResourceManager("WindowsGSM.Localization.Strings.es", typeof(LocalizationManager).Assembly);
        private static readonly Dictionary<string, string> LocalizedResourcePrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "fr", "fr." },
            { "de", "de." },
            { "it", "it." },
            { "pt", "pt." },
            { "nl", "nl." },
            { "pl", "pl." },
            { "ro", "ro." },
            { "ru", "ru." },
            { "uk", "uk." },
            { "ja", "ja." },
            { "zh-CN", "zh-CN." },
            { "ko", "ko." },
            { "tr", "tr." }
        };
        private static CultureInfo _systemUiCulture;

        public static string SelectedLanguage { get; private set; } = "system";

        public static void Initialize()
        {
            _systemUiCulture = CultureInfo.CurrentUICulture;
            string selectedLanguage = "system";

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                object savedLanguage = key?.GetValue(LanguageSettingName);
                if (savedLanguage != null)
                {
                    selectedLanguage = savedLanguage.ToString();
                    if (!IsSupportedLanguage(selectedLanguage))
                    {
                        Trace.TraceWarning("Ignoring unsupported WindowsGSM language setting '{0}'.", selectedLanguage);
                        selectedLanguage = "system";
                    }
                }
            }

            SelectedLanguage = selectedLanguage;
            ApplyCulture(selectedLanguage);
        }

        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A localization key is required.", nameof(key));
            }

            CultureInfo culture = CultureInfo.CurrentUICulture;
            string language = culture.TwoLetterISOLanguageName;
            string resourcePrefix;
            if (!LocalizedResourcePrefixes.TryGetValue(culture.Name, out resourcePrefix))
            {
                LocalizedResourcePrefixes.TryGetValue(language, out resourcePrefix);
            }

            if (!string.IsNullOrEmpty(resourcePrefix))
            {
                string localizedValue = EnglishResources.GetString(resourcePrefix + key, CultureInfo.InvariantCulture);
                if (localizedValue != null)
                {
                    return localizedValue;
                }
            }

            ResourceManager resources = language.Equals("es", StringComparison.OrdinalIgnoreCase)
                ? SpanishResources
                : EnglishResources;
            string value = resources.GetString(key, CultureInfo.InvariantCulture);
            if (value == null && resources != EnglishResources)
            {
                value = EnglishResources.GetString(key, CultureInfo.InvariantCulture);
            }

            if (value == null)
            {
                throw new MissingManifestResourceException(string.Format(CultureInfo.InvariantCulture, "Localization resource '{0}' was not found.", key));
            }

            return value;
        }

        public static void SaveLanguage(string language)
        {
            if (!IsSupportedLanguage(language))
            {
                throw new ArgumentOutOfRangeException(nameof(language), language, "The selected language is not supported.");
            }

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("The WindowsGSM settings registry key could not be opened.");
                }

                key.SetValue(LanguageSettingName, language, RegistryValueKind.String);
            }

            SelectedLanguage = language;
        }

        private static bool IsSupportedLanguage(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return false;
            }

            return language == "system"
                || language == "en"
                || language == "es"
                || LocalizedResourcePrefixes.ContainsKey(language);
        }

        private static void ApplyCulture(string language)
        {
            CultureInfo culture = language == "system"
                ? _systemUiCulture
                : CultureInfo.GetCultureInfo(language);

            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [MarkupExtensionReturnType(typeof(string))]
    public sealed class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return LocalizationManager.Get(Key);
        }
    }
}
