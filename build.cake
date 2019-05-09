var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

var PROJECT_DIR = Context.Environment.WorkingDirectory.FullPath + "/";
var BIN_DIR = PROJECT_DIR + "bin/" + configuration + "/";
var SOLUTION_FILE = "./src/pdfcrowd.csproj";

Task("Clean")
    .Description("Deletes all files in the BIN directory")
    .Does(() =>
    {
        CleanDirectory(BIN_DIR);
    });

Task("NuGetRestore")
    .Description("Restores NuGet Packages")
    .Does(() =>
    {
        DotNetCoreRestore(SOLUTION_FILE);
    });

Task("Build")
    .Description("Builds the Solution")
    .IsDependentOn("NuGetRestore")
    .Does(() =>
    {
        MSBuild(SOLUTION_FILE, CreateSettings());
    });

MSBuildSettings CreateSettings()
{
    var settings = new MSBuildSettings { Verbosity = Verbosity.Minimal, Configuration = configuration };

    // Only needed when packaging
    settings.WithProperty("DebugType", "pdbonly");

    if (IsRunningOnWindows())
    {
        // Find MSBuild for Visual Studio 2019 and newer
        DirectoryPath vsLatest = VSWhereLatest();
        FilePath msBuildPath = vsLatest?.CombineWithFilePath("./MSBuild/Current/Bin/MSBuild.exe");

        // Find MSBuild for Visual Studio 2017
        if (msBuildPath != null && !FileExists(msBuildPath))
            msBuildPath = vsLatest.CombineWithFilePath("./MSBuild/15.0/Bin/MSBuild.exe");

        // Have we found MSBuild yet?
        if (!FileExists(msBuildPath))
        {
            throw new Exception($"Failed to find MSBuild: {msBuildPath}");
        }

        Information("Building using MSBuild at " + msBuildPath);
        settings.ToolPath = msBuildPath;
    }
    else
        settings.ToolPath = Context.Tools.Resolve("msbuild");

    return settings;
}

Task("Rebuild")
    .Description("Rebuilds all versions of the framework")
    .IsDependentOn("Clean")
    .IsDependentOn("Build");
Task("Default")
    .Description("Builds all versions of the framework")
    .IsDependentOn("Build");

RunTarget(target);
