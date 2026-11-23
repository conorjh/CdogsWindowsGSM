using System;
using System.Threading.Tasks;
using System.Windows;
using MahApps.Metro.Controls.Dialogs;

namespace WindowsGSM.Functions
{
    public static class UI
    {
        public static bool HasExplicitMinecraftEulaIntegrationTestConsent()
        {
            return HasExplicitMinecraftIntegrationTestConsent("CDOGS_GAME_INTEGRATION_MINECRAFT_EULA_CONSENT");
        }

        public static bool HasExplicitMinecraftBedrockEulaAndPrivacyIntegrationTestConsent()
        {
            return HasExplicitMinecraftIntegrationTestConsent("CDOGS_GAME_INTEGRATION_MINECRAFT_BEDROCK_EULA_PRIVACY_CONSENT");
        }

        private static bool HasExplicitMinecraftIntegrationTestConsent(string consentVariable)
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CDOGS_GAME_INTEGRATION_GAMES"))
                && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CDOGS_GAME_INTEGRATION_ROOT"))
                && string.Equals(
                    Environment.GetEnvironmentVariable(consentVariable),
                    "I_HAVE_READ_AND_ACCEPTED",
                    StringComparison.Ordinal);
        }

        // Create Yes or No Prompt V1
        public static async Task<bool> CreateYesNoPromptV1(string title, string message, string affirmativeButtonText, string negativeButtonText)
        {
            if (HasExplicitMinecraftEulaIntegrationTestConsent())
            {
                return true;
            }

            return await ShowYesNoPromptV1(title, message, affirmativeButtonText, negativeButtonText);
        }

        private static async Task<bool> ShowYesNoPromptV1(string title, string message, string affirmativeButtonText, string negativeButtonText)
        {
            if (Application.Current == null)
            {
                return false;
            }

            return await Application.Current.Dispatcher.Invoke(async () =>
            {
                var WindowsGSM = (MainWindow)Application.Current.MainWindow;
                var settings = new MetroDialogSettings
                {
                    AffirmativeButtonText = affirmativeButtonText,
                    NegativeButtonText = negativeButtonText,
                    DefaultButtonFocus = MessageDialogResult.Affirmative
                };

                var result = await WindowsGSM.ShowMessageAsync(title, message, MessageDialogStyle.AffirmativeAndNegative, settings);
                return result == MessageDialogResult.Affirmative;
            });
        }
    }
}
