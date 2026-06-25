namespace Yoji.U3DAILinker.Settings
{
    internal interface IResolvedPackagePathProvider
    {
        string GetResolvedPath(string packageName);
    }
}
