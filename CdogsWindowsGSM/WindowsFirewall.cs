using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsGSM
{
    class WindowsFirewall
    {
        private const string ActiveProfilesScript = "$profiles = @(Get-NetConnectionProfile -ErrorAction Stop | ForEach-Object { switch ([int]$_.NetworkCategory) { 0 { 'Public' } 1 { 'Private' } 2 { 'Domain' } default { throw 'Unsupported network profile.' } } } | Select-Object -Unique); if ($profiles.Count -eq 0) { throw 'No active Windows network profile could be determined.' }; ";
        private readonly string _name;
        private readonly string _path;

        public WindowsFirewall(string name, string path)
        {
            _name = name;
            _path = path;
        }

        public async Task<bool> IsRuleExist()
        {
            if (Functions.ServerPath.IsTestRootOverrideActive)
            {
                return true;
            }

            try
            {
                string script = "$ErrorActionPreference = 'Stop'; " + ActiveProfilesScript +
                    $"$path = {PowerShellLiteral(GetFullPath())}; " +
                    "$filters = @(Get-NetFirewallApplicationFilter -PolicyStore PersistentStore -Program $path -ErrorAction Stop); " +
                    "foreach ($filter in $filters) { " +
                    "$rules = @(Get-NetFirewallRule -AssociatedNetFirewallApplicationFilter $filter -PolicyStore PersistentStore -ErrorAction Stop); " +
                    "foreach ($rule in $rules) { " +
                    "if ([string]$rule.Enabled -eq 'True' -and [string]$rule.Direction -eq 'Inbound' -and [string]$rule.Action -eq 'Allow') { " +
                    "$ruleProfiles = @(([string]$rule.Profile -split '[,\\s]+')); " +
                    "$matchingProfiles = @($ruleProfiles | Where-Object { $_ -eq 'Any' -or $profiles -contains $_ }); " +
                    "if ($matchingProfiles.Count -gt 0) { [Console]::Out.Write('True'); exit 0 } " +
                    "} } }; [Console]::Out.Write('False');";

                string output = await RunPowerShellAsync(script);
                if (!bool.TryParse(output, out bool exists))
                {
                    throw new InvalidOperationException($"Unexpected firewall query response: {output}");
                }

                return exists;
            }
            catch (Exception exception) when (IsFirewallException(exception))
            {
                LogFirewallFailure("check", exception);
                return false;
            }
        }

        public async Task<bool> AddRule()
        {
            if (Functions.ServerPath.IsTestRootOverrideActive)
            {
                return true;
            }

            try
            {
                if (await IsRuleExist())
                {
                    return true;
                }

                string ruleName = GetRuleName();
                string displayName = string.IsNullOrWhiteSpace(_name) ? System.IO.Path.GetFileName(GetFullPath()) : _name;
                string script = "$ErrorActionPreference = 'Stop'; " + ActiveProfilesScript +
                    $"$path = {PowerShellLiteral(GetFullPath())}; " +
                    $"$ruleName = {PowerShellLiteral(ruleName)}; " +
                    $"$displayName = {PowerShellLiteral(displayName)}; " +
                    "Remove-NetFirewallRule -Name $ruleName -PolicyStore PersistentStore -ErrorAction SilentlyContinue | Out-Null; " +
                    "New-NetFirewallRule -PolicyStore PersistentStore -Name $ruleName -DisplayName $displayName -Direction Inbound -Action Allow -Program $path -Enabled True -Profile $profiles -ErrorAction Stop | Out-Null; " +
                    "[Console]::Out.Write('True');";

                string output = await RunPowerShellAsync(script);
                if (!bool.TryParse(output, out bool added))
                {
                    throw new InvalidOperationException($"Unexpected firewall add response: {output}");
                }

                return added;
            }
            catch (Exception exception) when (IsFirewallException(exception))
            {
                LogFirewallFailure("add", exception);
                return false;
            }
        }

        public async Task<bool> RemoveRule()
        {
            if (Functions.ServerPath.IsTestRootOverrideActive)
            {
                return true;
            }

            try
            {
                string script = "$ErrorActionPreference = 'Stop'; " +
                    $"Remove-NetFirewallRule -Name {PowerShellLiteral(GetRuleName())} -PolicyStore PersistentStore -ErrorAction SilentlyContinue | Out-Null; " +
                    "[Console]::Out.Write('True');";
                await RunPowerShellAsync(script);
                return true;
            }
            catch (Exception exception) when (IsFirewallException(exception))
            {
                LogFirewallFailure("remove", exception);
                return false;
            }
        }

        public async Task RemoveRuleEx()
        {
            if (Functions.ServerPath.IsTestRootOverrideActive)
            {
                return;
            }

            try
            {
                string path = GetFullPath();
                string script = "$ErrorActionPreference = 'Stop'; " + ActiveProfilesScript +
                    $"$needle = {PowerShellLiteral(path)}; " +
                    "$filters = @(Get-NetFirewallApplicationFilter -PolicyStore PersistentStore -All -ErrorAction Stop); " +
                    "foreach ($filter in $filters) { " +
                    "if (![string]::IsNullOrWhiteSpace([string]$filter.Program) -and ([string]$filter.Program).IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { " +
                    "$rules = @(Get-NetFirewallRule -AssociatedNetFirewallApplicationFilter $filter -PolicyStore PersistentStore -ErrorAction Stop); " +
                    "foreach ($rule in $rules) { " +
                    "$ruleProfiles = @(([string]$rule.Profile -split '[,\\s]+')); " +
                    "$matchingProfiles = @($ruleProfiles | Where-Object { $_ -eq 'Any' -or $profiles -contains $_ }); " +
                    "if ($matchingProfiles.Count -gt 0) { Remove-NetFirewallRule -Name $rule.Name -PolicyStore PersistentStore -Confirm:$false -ErrorAction Stop | Out-Null } " +
                    "} } };";

                await RunPowerShellAsync(script);
            }
            catch (Exception exception) when (IsFirewallException(exception))
            {
                LogFirewallFailure("remove by path", exception);
            }
        }

        private string GetFullPath()
        {
            return System.IO.Path.GetFullPath(_path);
        }

        private string GetRuleName()
        {
            string normalizedPath = GetFullPath().ToUpperInvariant();
            byte[] pathHash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
            return "WindowsGSM-" + Convert.ToHexString(pathHash, 0, 12);
        }

        private static string PowerShellLiteral(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static async Task<string> RunPowerShellAsync(string script)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(script);

            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Windows PowerShell did not start.");
                }

                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> standardError = process.StandardError.ReadToEndAsync();
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                        throw new TimeoutException("Windows Firewall PowerShell command timed out.");
                    }
                }

                string output = await standardOutput;
                string error = await standardError;
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Windows Firewall PowerShell command failed with exit code {process.ExitCode}. {error} {output}".Trim());
                }

                return output.Trim();
            }
        }

        private static bool IsFirewallException(Exception exception)
        {
            return exception is ArgumentException || exception is IOException || exception is InvalidOperationException ||
                exception is NotSupportedException || exception is OperationCanceledException || exception is TimeoutException ||
                exception is UnauthorizedAccessException || exception is Win32Exception;
        }

        private static void LogFirewallFailure(string operation, Exception exception)
        {
            Debug.WriteLine($"Could not {operation} Windows Firewall rule: {exception}");
        }
    }
}
