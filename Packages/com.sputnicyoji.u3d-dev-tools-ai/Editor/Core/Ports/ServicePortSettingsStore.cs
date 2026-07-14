using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Yoji.EditorCore.Ports
{
    public static class ServicePortSettingsStore
    {
        public const string ProjectSettingsRelativePath = "ProjectSettings/YojiDevToolPorts.json";
        public const string UserSettingsRelativePath = "UserSettings/YojiDevToolPorts.user.json";

        public static ServicePortSettings LoadOrCreateProjectSettings(string projectRoot)
        {
            var normalizedRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot);
            var path = PortPersistencePaths.ProjectSettingsPath(normalizedRoot);
            var settings = LoadProjectSettings(path);
            var needsSave = false;

            if (settings == null)
            {
                settings = new ServicePortSettings();
                needsSave = true;
            }

            if (settings.SchemaVersion <= 0)
            {
                settings.SchemaVersion = 1;
                needsSave = true;
            }

            if (string.IsNullOrWhiteSpace(settings.ProjectId))
            {
                settings.ProjectId = PortPersistencePaths.GenerateStableProjectId(normalizedRoot);
                needsSave = true;
            }

            if (string.IsNullOrWhiteSpace(settings.Mode))
            {
                settings.Mode = "auto";
                needsSave = true;
            }

            if (settings.ServiceOverrides == null)
            {
                settings.ServiceOverrides = new List<ServicePortOverride>();
                needsSave = true;
            }

            if (needsSave)
                SaveProjectSettings(normalizedRoot, settings);

            return settings;
        }

        public static ServicePortUserSettings LoadUserSettings(string projectRoot)
        {
            var normalizedRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot);
            var settings = LoadUserSettingsInternal(PortPersistencePaths.UserSettingsPath(normalizedRoot));
            if (settings == null)
                settings = new ServicePortUserSettings();
            if (settings.SchemaVersion <= 0)
                settings.SchemaVersion = 1;
            return settings;
        }

        public static void SaveUserSettings(string projectRoot, ServicePortUserSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var normalizedRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot);
            var path = PortPersistencePaths.UserSettingsPath(normalizedRoot);
            var dto = new ServicePortUserSettingsDto
            {
                schemaVersion = settings.SchemaVersion > 0 ? settings.SchemaVersion : 1,
                overrideBasePort = settings.OverrideBasePort,
                preferLegacyPorts = settings.PreferLegacyPorts,
            };

            PortPersistenceIO.WriteJsonAtomic(path, JsonUtility.ToJson(dto, true));
        }

        public static void SaveProjectSettings(string projectRoot, ServicePortSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var normalizedRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot);
            var path = PortPersistencePaths.ProjectSettingsPath(normalizedRoot);
            var dto = ToProjectSettingsDto(settings);
            PortPersistenceIO.WriteJsonAtomic(path, JsonUtility.ToJson(dto, true));
        }

        public static ServicePortPolicy BuildPolicy(string projectRoot, ProjectIdentity identity)
        {
            var normalizedRoot = PortPersistencePaths.NormalizeProjectRoot(projectRoot);
            var projectSettings = identity != null && identity.ProjectSettings != null
                ? identity.ProjectSettings
                : LoadOrCreateProjectSettings(normalizedRoot);
            var userSettings = identity != null && identity.UserSettings != null
                ? identity.UserSettings
                : LoadUserSettings(normalizedRoot);
            var projectId = identity != null && !string.IsNullOrWhiteSpace(identity.ProjectId)
                ? identity.ProjectId
                : projectSettings.ProjectId;

            return new ServicePortPolicy
            {
                ProjectRoot = normalizedRoot,
                ProjectId = projectId,
                Mode = string.Equals(projectSettings.Mode, "fixedProject", StringComparison.OrdinalIgnoreCase)
                    ? ServicePortMode.FixedProject
                    : ServicePortMode.Auto,
                PreferredBasePort = projectSettings.PreferredBasePort,
                OverrideBasePort = userSettings.OverrideBasePort,
                PreferLegacyPorts = userSettings.PreferLegacyPorts,
            };
        }

        private static ServicePortSettings LoadProjectSettings(string path)
        {
            var dto = PortPersistenceIO.ReadJson<ProjectSettingsDto>(path);
            if (dto == null)
                return null;

            return FromProjectSettingsDto(dto);
        }

        private static ServicePortUserSettings LoadUserSettingsInternal(string path)
        {
            var dto = PortPersistenceIO.ReadJson<ServicePortUserSettingsDto>(path);
            if (dto == null)
                return null;

            return new ServicePortUserSettings
            {
                SchemaVersion = dto.schemaVersion > 0 ? dto.schemaVersion : 1,
                OverrideBasePort = dto.overrideBasePort,
                PreferLegacyPorts = dto.preferLegacyPorts,
            };
        }

        private static ServicePortSettings FromProjectSettingsDto(ProjectSettingsDto dto)
        {
            var settings = new ServicePortSettings
            {
                SchemaVersion = dto.schemaVersion > 0 ? dto.schemaVersion : 1,
                ProjectId = dto.projectId ?? string.Empty,
                Mode = string.Equals(dto.mode, "fixedProject", StringComparison.OrdinalIgnoreCase) ? "fixedProject" : "auto",
                PreferredBasePort = dto.preferredBasePort,
                ServiceOverrides = new List<ServicePortOverride>(),
            };

            if (dto.serviceOverrides != null)
            {
                for (var i = 0; i < dto.serviceOverrides.Length; i++)
                {
                    var overrideDto = dto.serviceOverrides[i];
                    if (overrideDto == null)
                        continue;

                    settings.ServiceOverrides.Add(new ServicePortOverride
                    {
                        ServiceId = overrideDto.serviceId ?? string.Empty,
                        OverrideBasePort = overrideDto.overrideBasePort,
                        PreferLegacyPorts = overrideDto.preferLegacyPorts,
                    });
                }
            }

            return settings;
        }

        private static ProjectSettingsDto ToProjectSettingsDto(ServicePortSettings settings)
        {
            var overrides = new List<ServicePortOverrideDto>();
            if (settings.ServiceOverrides != null)
            {
                for (var i = 0; i < settings.ServiceOverrides.Count; i++)
                {
                    var serviceOverride = settings.ServiceOverrides[i];
                    if (serviceOverride == null)
                        continue;

                    overrides.Add(new ServicePortOverrideDto
                    {
                        serviceId = serviceOverride.ServiceId ?? string.Empty,
                        overrideBasePort = serviceOverride.OverrideBasePort,
                        preferLegacyPorts = serviceOverride.PreferLegacyPorts,
                    });
                }
            }

            return new ProjectSettingsDto
            {
                schemaVersion = settings.SchemaVersion > 0 ? settings.SchemaVersion : 1,
                projectId = settings.ProjectId ?? string.Empty,
                mode = string.Equals(settings.Mode, "fixedProject", StringComparison.OrdinalIgnoreCase) ? "fixedProject" : "auto",
                preferredBasePort = settings.PreferredBasePort,
                serviceOverrides = overrides.ToArray(),
            };
        }
    }

    internal static class PortPersistencePaths
    {
        public static string ProjectSettingsPath(string projectRoot)
        {
            return Path.Combine(ToFileSystemPath(projectRoot), ServicePortSettingsStore.ProjectSettingsRelativePath);
        }

        public static string UserSettingsPath(string projectRoot)
        {
            return Path.Combine(ToFileSystemPath(projectRoot), ServicePortSettingsStore.UserSettingsRelativePath);
        }

        public static string ProjectPortsPath(string projectRoot)
        {
            return Path.Combine(ToFileSystemPath(projectRoot), ".u3d-ai-linker", "ports.json");
        }

        public static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
                return string.Empty;

            return Path.GetFullPath(projectRoot).Replace('\\', '/');
        }

        public static string ToFileSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }

        public static string FallbackProjectName(string projectRoot)
        {
            var trimmedRoot = NormalizeProjectRoot(projectRoot).TrimEnd('/');
            var name = Path.GetFileName(trimmedRoot);
            if (string.IsNullOrWhiteSpace(name))
                return "Project";

            return name;
        }

        public static string GenerateStableProjectId(string projectRoot)
        {
            var normalizedRoot = NormalizeProjectRoot(projectRoot).ToLowerInvariant();
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedRoot));
                var guidBytes = new byte[16];
                Array.Copy(hash, guidBytes, guidBytes.Length);
                return new Guid(guidBytes).ToString("N");
            }
        }
    }

    internal static class PortPersistenceIO
    {
        private const int k_WriteReplaceAttempts = 6;
        private const int k_WriteRetryDelayMs = 50;
        private const int c_HResultSharingViolation = unchecked((int)0x80070020);
        private const int c_HResultLockViolation = unchecked((int)0x80070021);
        private const int c_HResultAccessDenied = unchecked((int)0x80070005);
        private const int c_HResultUnableToRemoveReplaced = unchecked((int)0x80070497);
        private const int c_HResultUnableToMoveReplacement = unchecked((int)0x80070498);

        public static T ReadJson<T>(string path) where T : class
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;

                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void WriteJsonAtomic(string path, string json, bool acquireLock = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is required", nameof(path));

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = Path.Combine(string.IsNullOrEmpty(directory) ? Path.GetTempPath() : directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            IDisposable writeLock = null;

            try
            {
                if (acquireLock)
                    writeLock = PortPersistenceLock.Acquire(path);

                File.WriteAllText(tempPath, json ?? string.Empty, new UTF8Encoding(false));
                ReplaceOrMoveWithRetry(tempPath, path);
            }
            finally
            {
                if (writeLock != null)
                    writeLock.Dispose();

                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void ReplaceOrMoveWithRetry(string tempPath, string path)
        {
            Exception lastError = null;
            for (var attempt = 1; attempt <= k_WriteReplaceAttempts; attempt++)
            {
                try
                {
                    ReplaceOrMove(tempPath, path);
                    return;
                }
                catch (Exception e) when (IsTransientWriteError(e) && attempt < k_WriteReplaceAttempts)
                {
                    lastError = e;
                    Thread.Sleep(k_WriteRetryDelayMs * attempt);
                }
                catch (Exception e) when (IsTransientWriteError(e))
                {
                    lastError = e;
                }
            }

            throw new IOException("failed to atomically replace JSON file after retries: " + path, lastError);
        }

        private static void ReplaceOrMove(string tempPath, string path)
        {
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        private static bool IsTransientWriteError(Exception e)
        {
            if (e is UnauthorizedAccessException)
                return true;

            if (!(e is IOException))
                return false;

            return e.HResult == c_HResultSharingViolation ||
                   e.HResult == c_HResultLockViolation ||
                   e.HResult == c_HResultAccessDenied ||
                   e.HResult == c_HResultUnableToRemoveReplaced ||
                   e.HResult == c_HResultUnableToMoveReplacement;
        }
    }

    internal static class PortPersistenceLock
    {
        public static IDisposable Acquire(string targetPath)
        {
            var mutexScope = TryAcquireNamedMutex(targetPath);
            if (mutexScope != null)
                return mutexScope;

            return AcquireFileLock(targetPath);
        }

        private static IDisposable TryAcquireNamedMutex(string targetPath)
        {
            try
            {
                var mutexName = "Global\\Yoji_U3D_Dev_Tools_AI_" + BuildMutexSuffix(targetPath);
                var mutex = new Mutex(false, mutexName);
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("failed to acquire named mutex: " + mutexName);

                    return new MutexScope(mutex);
                }
                catch (AbandonedMutexException)
                {
                    return new MutexScope(mutex);
                }
                catch (TimeoutException)
                {
                    mutex.Dispose();
                    throw;
                }
                catch
                {
                    mutex.Dispose();
                    return null;
                }
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        private static IDisposable AcquireFileLock(string targetPath)
        {
            var lockPath = targetPath + ".lock";
            var directory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            Exception lastError = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return new FileLockScope(stream);
                }
                catch (IOException e)
                {
                    lastError = e;
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException e)
                {
                    lastError = e;
                    Thread.Sleep(50);
                }
            }

            throw new IOException("failed to acquire file lock: " + lockPath, lastError);
        }

        private static string BuildMutexSuffix(string targetPath)
        {
            var normalized = string.IsNullOrWhiteSpace(targetPath) ? string.Empty : targetPath.ToLowerInvariant();
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var builder = new StringBuilder(32);
                for (var i = 0; i < 16 && i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private sealed class MutexScope : IDisposable
        {
            private Mutex m_Mutex;

            public MutexScope(Mutex mutex)
            {
                m_Mutex = mutex;
            }

            public void Dispose()
            {
                if (m_Mutex == null)
                    return;

                try
                {
                    m_Mutex.ReleaseMutex();
                }
                catch
                {
                }

                m_Mutex.Dispose();
                m_Mutex = null;
            }
        }

        private sealed class FileLockScope : IDisposable
        {
            private FileStream m_Stream;

            public FileLockScope(FileStream stream)
            {
                m_Stream = stream;
            }

            public void Dispose()
            {
                if (m_Stream == null)
                    return;

                m_Stream.Dispose();
                m_Stream = null;
            }
        }

    }

    [Serializable]
    internal sealed class ProjectSettingsDto
    {
        public int schemaVersion = 1;
        public string projectId = string.Empty;
        public string mode = "auto";
        public int preferredBasePort = 0;
        public ServicePortOverrideDto[] serviceOverrides = new ServicePortOverrideDto[0];
    }

    [Serializable]
    internal sealed class ServicePortOverrideDto
    {
        public string serviceId = string.Empty;
        public int overrideBasePort = 0;
        public bool preferLegacyPorts = true;
    }

    [Serializable]
    internal sealed class ServicePortUserSettingsDto
    {
        public int schemaVersion = 1;
        public int overrideBasePort = 0;
        public bool preferLegacyPorts = true;
    }
}
