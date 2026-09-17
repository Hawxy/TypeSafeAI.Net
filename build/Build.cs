using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Tools.GitVersion;
using Fallout.Common.Utilities.Collections;
using Fallout.Solutions;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "continuous",
    GitHubActionsImage.UbuntuLatest,
    GitHubActionsImage.WindowsLatest,
    OnPushBranches = ["main"],
    OnPullRequestBranches = ["main"],
    InvokedTargets = [nameof(Test), nameof(Pack)],
    ImportSecrets = [nameof(TypeSafeApiKey)],
    PublishArtifacts = true,
    FetchDepth = 0)]
class Build : FalloutBuild
{
    public static int Main() => Execute<Build>(x => x.Test);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("NuGet source to push packages to")]
    readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("API key used to push NuGet packages")]
    [Secret]
    readonly string NuGetApiKey;

    [Parameter("TypeSafe API key; enables the LiveTest target")]
    [Secret]
    readonly string TypeSafeApiKey;

    [Parameter("Runtime identifier for the AOT smoke publish (defaults to the current platform)")]
    readonly string AotRuntime;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion(NoFetch = true)] readonly GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath SamplesDirectory => RootDirectory / "samples";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PackagesDirectory => ArtifactsDirectory / "packages";
    AbsolutePath TestResultsDirectory => ArtifactsDirectory / "test-results";
    AbsolutePath AotDirectory => ArtifactsDirectory / "aot";

    IEnumerable<AbsolutePath> UnitTestProjects =>
        TestsDirectory.GlobFiles("*/*.csproj").Where(p => !p.Name.Contains("LiveTests"));

    AbsolutePath LiveTestProject => TestsDirectory / "TypeSafeAI.LiveTests" / "TypeSafeAI.LiveTests.csproj";
    AbsolutePath AotSmokeProject => SamplesDirectory / "TicketTriage" / "TicketTriage.csproj";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            SamplesDirectory.GlobDirectories("**/bin", "**/obj").ForEach(d => d.DeleteDirectory());
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetToolRestore();
            DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .SetContinuousIntegrationBuild(IsServerBuild)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Produces(TestResultsDirectory / "*.trx")
        .Executes(() =>
        {
            TestResultsDirectory.CreateDirectory();
            DotNetTest(s => s
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetProcessAdditionalArguments("--", "--report-trx", "--results-directory", TestResultsDirectory)
                .CombineWith(UnitTestProjects, (cs, project) => cs.SetProjectFile(project)));
        });

    Target LiveTest => _ => _
        .DependsOn(Compile)
        .OnlyWhenDynamic(() => !string.IsNullOrEmpty(TypeSafeApiKey))
        .Produces(TestResultsDirectory / "live-*.trx")
        .Executes(() =>
        {
            TestResultsDirectory.CreateDirectory();
            DotNetTest(s => s
                .SetProjectFile(LiveTestProject)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetProcessEnvironmentVariable("TYPESAFE_API_KEY", TypeSafeApiKey)
                .SetProcessAdditionalArguments("--", "--report-trx", "--report-trx-filename", "live-results.trx", "--results-directory", TestResultsDirectory));
        });

    Target AotSmoke => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            AotDirectory.CreateOrCleanDirectory();
            DotNetPublish(s => s
                .SetProject(AotSmokeProject)
                .SetConfiguration(Configuration.Release)
                .SetRuntime(AotRuntime ?? CurrentRuntimeIdentifier)
                .SetOutput(AotDirectory)
                .SetProperty("PublishAot", "true")
                .SetProperty("TreatWarningsAsErrors", "true"));
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Produces(PackagesDirectory / "*.nupkg", PackagesDirectory / "*.snupkg")
        .Executes(() =>
        {
            PackagesDirectory.CreateOrCleanDirectory();
            DotNetPack(s => s
                .SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetVersion(GitVersion.NuGetVersionV2)
                .SetPackageReleaseNotes(ReleaseNotes)
                .SetOutputDirectory(PackagesDirectory)
                .EnableNoBuild()
                .EnableNoRestore());
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .Requires(() => NuGetApiKey)
        .Requires(() => Configuration.Equals(Configuration.Release))
        .Executes(() =>
        {
            DotNetNuGetPush(s => s
                    .SetSource(NuGetSource)
                    .SetApiKey(NuGetApiKey)
                    .EnableSkipDuplicate()
                    .CombineWith(PackagesDirectory.GlobFiles("*.nupkg"), (cs, package) => cs.SetTargetPath(package)),
                degreeOfParallelism: 3,
                completeOnFailure: true);
        });

    static string CurrentRuntimeIdentifier =>
        OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? "osx-arm64" : "linux-x64";

    // Pulls the "Unreleased" section out of CHANGELOG.md for the package release notes.
    string ReleaseNotes
    {
        get
        {
            var changelog = RootDirectory / "CHANGELOG.md";
            if (!changelog.FileExists())
            {
                return string.Empty;
            }

            var lines = changelog.ReadAllLines();
            var start = Array.FindIndex(lines, l => l.StartsWith("## "));
            if (start < 0)
            {
                return string.Empty;
            }

            var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## "));
            var section = end < 0 ? lines[(start + 1)..] : lines[(start + 1)..end];
            return string.Join('\n', section).Trim();
        }
    }
}
