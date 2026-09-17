using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Solutions;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "Build & Test",
    GitHubActionsImage.UbuntuLatest,
    OnPushBranches = ["main"],
    OnPullRequestBranches = ["main"],
    InvokedTargets = [nameof(Test), nameof(AotSmoke)],
    ImportSecrets = [nameof(TypeSafeApiKey)])]
[TrustedPublishingGitHubActions(
    "Manual Nuget Push",
    GitHubActionsImage.UbuntuLatest,
    On = [GitHubActionsTrigger.WorkflowDispatch],
    InvokedTargets = [nameof(NugetPush)],
    NugetUser = "${{ secrets.NUGET_USER }}")]
class Build : FalloutBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Solution] readonly Solution Solution;

    [Parameter("TypeSafe API key; when set, the live API tests run instead of being skipped")]
    [Secret]
    readonly string TypeSafeApiKey;

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration("Release")
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Release, matching Compile: reuses its output and tests the configuration that ships.
            // The live tests skip themselves unless TYPESAFE_API_KEY is present.
            DotNetTest(s => s
                .AddProcessAdditionalArguments("--solution", Solution)
                .AddProcessAdditionalArguments("--configuration", "Release")
                .When(_ => !string.IsNullOrEmpty(TypeSafeApiKey), s => s
                    .SetProcessEnvironmentVariable("TYPESAFE_API_KEY", TypeSafeApiKey)));
        });

    Target AotSmoke => _ => _
        .Executes(() =>
        {
            // Publishes the sample with native AOT and runs it, which fails on any trim or AOT regression.
            var project = Solution.AllProjects.Single(x => x.Name == "TicketTriage");
            var output = ArtifactsDirectory / "aot-smoke";
            DotNetPublish(_ => _
                .SetProject(project)
                .SetConfiguration("Release")
                .SetOutput(output));

            var exe = output / (OperatingSystem.IsWindows() ? "TicketTriage.exe" : "TicketTriage");
            ProcessTasks.StartProcess(exe, workingDirectory: output).AssertZeroExitCode();
        });

    static readonly string[] PackableProjects =
    [
        "TypeSafeAI",
        "TypeSafeAI.Extensions.AI",
    ];

    Target NugetPack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            foreach (var name in PackableProjects)
            {
                var project = Solution.AllProjects.Single(x => x.Name == name);
                DotNetPack(_ => _
                    .SetProject(project)
                    .SetConfiguration("Release")
                    .EnableContinuousIntegrationBuild()
                    .SetOutputDirectory(ArtifactsDirectory));
            }
        });

    [Parameter("NuGet API key, short-lived key issued by NuGet/login via trusted publishing")] [Secret] readonly string NugetApiKey;

    Target NugetPush => _ => _
        .DependsOn(NugetPack)
        .Requires(() => !string.IsNullOrEmpty(NugetApiKey))
        .Executes(() =>
        {
            DotNetNuGetPush(_ => _
                .SetSource("https://api.nuget.org/v3/index.json")
                .SetTargetPath(ArtifactsDirectory / "*.nupkg")
                .EnableSkipDuplicate()
                .EnableNoSymbols()
                .SetApiKey(NugetApiKey));
        });
}
