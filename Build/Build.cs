using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Coverlet;
using Nuke.Common.Tools.DotCover;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.ReportGenerator;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.ControlFlow;
using static Nuke.Common.IO.CompressionTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.Git.GitTasks;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.ReportGenerator.ReportGeneratorTasks;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
[GitHubActions(
	"continuous",
	GitHubActionsImage.UbuntuLatest,
	On = new[] { GitHubActionsTrigger.Push },
	InvokedTargets = new[] { nameof(Compile) },
	ImportGitHubTokenAs = nameof(GitHubToken))]
internal class Build : NukeBuild
{
	#region Private Fields

	private const string DevelopBranch = "develop";

	private const string HotfixBranchPrefix = "hotfix";

	private const string MasterBranch = "main";

	private const string ReleaseBranchPrefix = "release";

	private const string FeatureBranch = "feature";

	[Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
	private readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

	[Parameter] private readonly string GitHubToken;

	[Required]
	[GitRepository]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Redundancy", "RCS1213:Remove unused member declaration.", Justification = "<Pending>")]
	private readonly GitRepository GitRepository;

	//[Required] [GitVersion] private readonly GitVersion GitVersion;

	[Parameter] private readonly bool IgnoreFailedSources;

	[Required] [Solution] private readonly Solution Solution;

	#endregion Private Fields

	#region Private Properties

	private Target Clean => _ => _.Executes(() => EnsureCleanDirectory(OutputDirectory));

	private Target Compile =>
		_ => _
			.DependsOn(Restore)
			.Executes(() => DotNetBuild(s => s
				.SetProjectFile(Solution)
				.SetConfiguration(Configuration)
				//.SetAssemblyVersion(GitVersion.AssemblySemVer)
				//.SetFileVersion(GitVersion.AssemblySemFileVer)
				//.SetInformationalVersion(GitVersion.InformationalVersion)
				.EnableNoRestore()));

	private AbsolutePath OutputDirectory => RootDirectory / "Output";

	private Target Restore =>
		_ => _
			.DependsOn(Clean)
			.Executes(() => DotNetRestore(s => s
			   .SetProjectFile(Solution)
			   .SetIgnoreFailedSources(IgnoreFailedSources)));

	private AbsolutePath SourceDirectory => RootDirectory / "Source";

	private Target Test =>
		_ => _
			.DependsOn(Compile)
			.Executes(() => DotNetTest(s => s.SetProjectFile(Solution)));

	private AbsolutePath TestsDirectory => RootDirectory / "Tests";

	#endregion Private Properties

	#region Public Methods

	public static int Main() => Execute<Build>(x => x.Test);

	#endregion Public Methods
}
