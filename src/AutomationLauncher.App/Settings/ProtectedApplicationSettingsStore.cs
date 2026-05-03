using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.ObjectModel;
using AutomationLauncher.Domain.Models;

namespace AutomationLauncher.App;

public sealed class ProtectedApplicationSettingsStore : IProtectedApplicationSettingsStore
{
    private const int PasswordIterations = 100_000;
    private const int KeySizeBytes = 32;

    public ProtectedApplicationSettingsStore(string settingsFilePath)
    {
        SettingsFilePath = settingsFilePath;
        CachedSettingsFilePath = Path.Combine(
            Path.GetDirectoryName(settingsFilePath) ?? AppContext.BaseDirectory,
            "settings-cache.json");
    }

    public string SettingsFilePath { get; }

    public string CachedSettingsFilePath { get; }

    public bool HasProtectedSettings()
    {
        return File.Exists(SettingsFilePath);
    }

    public bool TryLoadCachedSettings(out AutomationLauncherSettings? settings, out string errorMessage)
    {
        settings = null;
        errorMessage = string.Empty;

        if (!File.Exists(CachedSettingsFilePath))
        {
            errorMessage = "Cached settings were not found.";
            return false;
        }

        try
        {
            var json = File.ReadAllText(CachedSettingsFilePath);
            settings = JsonSerializer.Deserialize<AutomationLauncherSettings>(json, BuildSerializerOptions());
            if (settings is null)
            {
                errorMessage = "Cached settings file is invalid.";
                return false;
            }

            Normalize(settings);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public bool ValidatePasswordRequirements(string password, out string validationMessage)
    {
        if (password.Length < 12)
        {
            validationMessage = "Password must contain at least 12 characters.";
            return false;
        }

        if (!password.Any(char.IsUpper))
        {
            validationMessage = "Password must contain at least one uppercase letter.";
            return false;
        }

        if (!password.Any(char.IsLower))
        {
            validationMessage = "Password must contain at least one lowercase letter.";
            return false;
        }

        if (!password.Any(char.IsDigit))
        {
            validationMessage = "Password must contain at least one digit.";
            return false;
        }

        if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            validationMessage = "Password must contain at least one special character.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    public void Create(AutomationLauncherSettings settings, string password)
    {
        Save(settings, password);
    }

    public bool TryLoad(string password, out AutomationLauncherSettings? settings, out string errorMessage)
    {
        settings = null;
        errorMessage = string.Empty;

        if (!HasProtectedSettings())
        {
            errorMessage = "Protected settings were not found.";
            return false;
        }

        try
        {
            var json = File.ReadAllText(SettingsFilePath);
            var envelope = JsonSerializer.Deserialize<ProtectedSettingsEnvelope>(json);
            if (envelope is null)
            {
                errorMessage = "Protected settings file is invalid.";
                return false;
            }

            if (!VerifyPassword(password, envelope.PasswordSaltBase64, envelope.PasswordHashBase64))
            {
                errorMessage = "Incorrect password.";
                return false;
            }

            var decryptedJson = DecryptPayload(password, envelope.PayloadSaltBase64, envelope.PayloadIvBase64, envelope.EncryptedPayloadBase64);
            settings = JsonSerializer.Deserialize<AutomationLauncherSettings>(decryptedJson, BuildSerializerOptions());
            if (settings is null)
            {
                errorMessage = "Protected settings payload is empty.";
                return false;
            }

            Normalize(settings);
            return true;
        }
        catch (CryptographicException)
        {
            errorMessage = "Unable to decrypt protected settings with the provided password.";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public void Save(AutomationLauncherSettings settings, string password)
    {
        Normalize(settings);

        var directoryPath = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var passwordSalt = CreateRandomBytes(16);
        var payloadSalt = CreateRandomBytes(16);
        var iv = CreateRandomBytes(16);
        var payloadJson = JsonSerializer.Serialize(settings, BuildSerializerOptions());

        var envelope = new ProtectedSettingsEnvelope
        {
            Version = 1,
            PasswordSaltBase64 = Convert.ToBase64String(passwordSalt),
            PasswordHashBase64 = Convert.ToBase64String(HashPassword(password, passwordSalt)),
            PayloadSaltBase64 = Convert.ToBase64String(payloadSalt),
            PayloadIvBase64 = Convert.ToBase64String(iv),
            EncryptedPayloadBase64 = EncryptPayload(payloadJson, password, payloadSalt, iv)
        };

        var json = JsonSerializer.Serialize(envelope, BuildSerializerOptions());
        File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);

        var cachedSettingsJson = JsonSerializer.Serialize(settings, BuildSerializerOptions());
        File.WriteAllText(CachedSettingsFilePath, cachedSettingsJson, Encoding.UTF8);
    }

    private static JsonSerializerOptions BuildSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    private static void Normalize(AutomationLauncherSettings settings)
    {
        settings.Archive ??= new();
        settings.Project ??= new();
        settings.ControlFiles ??= new();
        settings.Startup ??= new();
        settings.Logging ??= new();
        settings.Ui ??= new();

        settings.Project.PowerShellScripts ??= new List<ProjectScriptEntry>();
        settings.Project.PowerShellScripts = settings.Project.PowerShellScripts
            .Where(entry => entry is not null)
            .Select(entry => new ProjectScriptEntry
            {
                Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
                Name = entry.Name?.Trim() ?? string.Empty,
                ScriptBody = entry.ScriptBody ?? string.Empty,
                TimeoutSeconds = entry.TimeoutSeconds < 1 ? 300 : entry.TimeoutSeconds,
                Parameters = new ObservableCollection<ProjectScriptParameterEntry>(NormalizeScriptParameters(entry.Parameters))
            })
            .ToList();

        var normalizedBindings = new Dictionary<string, ControlFileScriptBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in settings.ControlFiles.Bindings ?? Enumerable.Empty<ControlFileScriptBinding>())
        {
            if (binding is null)
            {
                continue;
            }

            var normalizedType = NormalizeControlFileType(binding.ControlFileType);
            if (normalizedType is null)
            {
                continue;
            }

            normalizedBindings[normalizedType] = new ControlFileScriptBinding
            {
                ControlFileType = normalizedType,
                PreExecutionSteps = new ObservableCollection<ControlFileScriptSequenceStep>(NormalizeSequenceSteps(binding.PreExecutionSteps)),
                PostExecutionSteps = new ObservableCollection<ControlFileScriptSequenceStep>(NormalizeSequenceSteps(binding.PostExecutionSteps))
            };
        }

        settings.ControlFiles.Bindings = ControlFileScriptBinding.KnownControlFileTypes
            .Select(type => normalizedBindings.TryGetValue(type, out var binding)
                ? binding
                : new ControlFileScriptBinding { ControlFileType = type })
            .ToList();

        settings.Startup.SplashBackgroundImagePath = string.IsNullOrWhiteSpace(settings.Startup.SplashBackgroundImagePath)
            ? string.Empty
            : settings.Startup.SplashBackgroundImagePath.Trim();

        settings.Startup.SequenceEntries ??= new List<StartupSequenceEntry>();
        settings.Startup.SequenceEntries = settings.Startup.SequenceEntries
            .Where(entry => entry is not null)
            .Select(entry => new StartupSequenceEntry
            {
                Alias = entry.Alias?.Trim() ?? string.Empty,
                ExecutablePath = entry.ExecutablePath?.Trim() ?? string.Empty,
                DelaySeconds = Math.Max(0, entry.DelaySeconds)
            })
            .ToList();

        if (settings.Logging.RetainedFileCountLimit < 1)
        {
            settings.Logging.RetainedFileCountLimit = 30;
        }

        settings.Logging.DirectoryPath = string.IsNullOrWhiteSpace(settings.Logging.DirectoryPath)
            ? "logs"
            : settings.Logging.DirectoryPath.Trim();

        settings.Logging.MinimumLevel = string.IsNullOrWhiteSpace(settings.Logging.MinimumLevel)
            ? "Information"
            : settings.Logging.MinimumLevel.Trim();

        settings.Ui.ControlFilesDirectory = string.IsNullOrWhiteSpace(settings.Ui.ControlFilesDirectory)
            ? string.Empty
            : settings.Ui.ControlFilesDirectory.Trim();

        if (!Enum.IsDefined(typeof(ArchiveBackupFlow), settings.Archive.BackupFlow))
        {
            settings.Archive.BackupFlow = ArchiveBackupFlow.TimestampedRetention;
        }

        if (settings.Archive.SuccessfulBackupRetentionCount < 0)
        {
            settings.Archive.SuccessfulBackupRetentionCount = 0;
        }
    }

    private static IEnumerable<ControlFileScriptSequenceStep> NormalizeSequenceSteps(IEnumerable<ControlFileScriptSequenceStep>? steps)
    {
        return (steps ?? Enumerable.Empty<ControlFileScriptSequenceStep>())
            .Where(step => step is not null)
            .Select(step => new ControlFileScriptSequenceStep
            {
                ScriptId = step.ScriptId?.Trim() ?? string.Empty,
                OnSuccess = Enum.IsDefined(typeof(ControlFileScriptOutcomeAction), step.OnSuccess)
                    ? step.OnSuccess
                    : ControlFileScriptOutcomeAction.RunNextScript,
                OnFailure = Enum.IsDefined(typeof(ControlFileScriptOutcomeAction), step.OnFailure)
                    ? step.OnFailure
                    : ControlFileScriptOutcomeAction.AbortControlFlow,
                ParameterOverrides = new ObservableCollection<ControlFileScriptParameterOverrideEntry>(NormalizeParameterOverrides(step.ParameterOverrides))
            });
    }

    private static IEnumerable<ControlFileScriptParameterOverrideEntry> NormalizeParameterOverrides(IEnumerable<ControlFileScriptParameterOverrideEntry>? overrides)
    {
        return (overrides ?? Enumerable.Empty<ControlFileScriptParameterOverrideEntry>())
            .Where(overrideEntry => overrideEntry is not null)
            .Select(overrideEntry => new ControlFileScriptParameterOverrideEntry
            {
                Name = overrideEntry.Name?.Trim() ?? string.Empty,
                Value = overrideEntry.Value ?? string.Empty
            })
            .Where(overrideEntry => !string.IsNullOrWhiteSpace(overrideEntry.Name));
    }

    private static IEnumerable<ProjectScriptParameterEntry> NormalizeScriptParameters(IEnumerable<ProjectScriptParameterEntry>? parameters)
    {
        return (parameters ?? Enumerable.Empty<ProjectScriptParameterEntry>())
            .Where(parameter => parameter is not null)
            .Select(parameter => new ProjectScriptParameterEntry
            {
                Name = parameter.Name?.Trim() ?? string.Empty,
                DefaultValue = parameter.DefaultValue ?? string.Empty
            })
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name));
    }

    private static string? NormalizeControlFileType(string? controlFileType)
    {
        if (string.IsNullOrWhiteSpace(controlFileType))
        {
            return null;
        }

        var normalized = controlFileType!.Trim().ToLowerInvariant();
        return ControlFileScriptBinding.KnownControlFileTypes.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    private static bool VerifyPassword(string password, string saltBase64, string expectedHashBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var expectedHash = Convert.FromBase64String(expectedHashBase64);
        var actualHash = HashPassword(password, salt);
        return FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] CreateRandomBytes(int size)
    {
        var buffer = new byte[size];
        using var generator = RandomNumberGenerator.Create();
        generator.GetBytes(buffer);
        return buffer;
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var difference = 0;
        for (var index = 0; index < left.Length; index++)
        {
            difference |= left[index] ^ right[index];
        }

        return difference == 0;
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        using var deriveBytes = new Rfc2898DeriveBytes(password, salt, PasswordIterations, HashAlgorithmName.SHA256);
        return deriveBytes.GetBytes(KeySizeBytes);
    }

    private static string EncryptPayload(string payloadJson, string password, byte[] salt, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey(password, salt);
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var encryptedBytes = encryptor.TransformFinalBlock(payloadBytes, 0, payloadBytes.Length);
        return Convert.ToBase64String(encryptedBytes);
    }

    private static string DecryptPayload(string password, string saltBase64, string ivBase64, string encryptedPayloadBase64)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey(password, Convert.FromBase64String(saltBase64));
        aes.IV = Convert.FromBase64String(ivBase64);
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var encryptedBytes = Convert.FromBase64String(encryptedPayloadBase64);
        var payloadBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
        return Encoding.UTF8.GetString(payloadBytes);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var deriveBytes = new Rfc2898DeriveBytes(password, salt, PasswordIterations, HashAlgorithmName.SHA256);
        return deriveBytes.GetBytes(KeySizeBytes);
    }

    private sealed class ProtectedSettingsEnvelope
    {
        public int Version { get; set; }

        public string PasswordSaltBase64 { get; set; } = string.Empty;

        public string PasswordHashBase64 { get; set; } = string.Empty;

        public string PayloadSaltBase64 { get; set; } = string.Empty;

        public string PayloadIvBase64 { get; set; } = string.Empty;

        public string EncryptedPayloadBase64 { get; set; } = string.Empty;
    }
}