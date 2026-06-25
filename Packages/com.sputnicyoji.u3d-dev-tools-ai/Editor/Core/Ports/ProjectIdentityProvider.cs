using System.IO;
using UnityEngine;

namespace Yoji.EditorCore.Ports
{
    public static class ProjectIdentityProvider
    {
        public static ProjectIdentity Current()
        {
            var projectRoot = PortPersistencePaths.NormalizeProjectRoot(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            var projectSettings = ServicePortSettingsStore.LoadOrCreateProjectSettings(projectRoot);
            var userSettings = ServicePortSettingsStore.LoadUserSettings(projectRoot);
            var projectName = Application.productName;

            if (string.IsNullOrWhiteSpace(projectName))
                projectName = PortPersistencePaths.FallbackProjectName(projectRoot);

            return new ProjectIdentity
            {
                ProjectRoot = projectRoot,
                ProjectId = projectSettings.ProjectId,
                ProjectName = projectName,
                ProjectSettings = projectSettings,
                UserSettings = userSettings,
            };
        }
    }
}
