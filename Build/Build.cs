/*

Copyright 2021 Karthik K Selvan, getkks@live.in

Unlicense

This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or distribute this
software, either in source code form or as a compiled binary, for any purpose,
commercial or non-commercial, and by any means.

In jurisdictions that recognize copyright laws, the author or authors of this
software dedicate any and all copyright interest in the software to the public
domain. We make this dedication for the benefit of the public at large and to
the detriment of our heirs and
successors. We intend this dedication to be an overt act of relinquishment in
perpetuity of all present and future rights to this software under copyright
law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

For more information, please refer to <http://unlicense.org/>

Project			: Build @ d:\Development\FSharp\CSVParser\Build

File			: Build.cs @ d:\Development\FSharp\CSVParser\Build\Build.cs
File Created	: Saturday, 20th February 2021 2:03:41 pm

Author			: Karthik K Selvan (getkks@live.in)

Last Modified	: Saturday, 27th February 2021 12:18:56 pm
Modified By		: Karthik K Selvan (getkks@live.in>)

Change History:

 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Coverlet;
using Nuke.Common.Tools.DotCover;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.MSBuild;
using Nuke.Common.Tools.ReportGenerator;
using Nuke.Common.Tools.ReSharper;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using Nuke.WebDocu;

using static Nuke.Common.ChangeLog.ChangelogTasks;
using static Nuke.Common.IO.CompressionTasks;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.Git.GitTasks;
using static Nuke.Common.Tools.ReportGenerator.ReportGeneratorTasks;

[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
[GitHubActions(
	"continuous",
	GitHubActionsImage.WindowsLatest,
	GitHubActionsImage.UbuntuLatest,
	GitHubActionsImage.MacOsLatest,
	OnPushBranchesIgnore = new[] { MainBranch, ReleaseBranchPrefix + "/*" },
	OnPullRequestBranches = new[] { DevelopBranch },
	PublishArtifacts = false,
	InvokedTargets = new[] { nameof(Test), nameof(Pack) })]
public partial class Build : NukeBuild
{
	#region Private Fields

	private const string DevelopBranch = "develop";
	private const string HotfixBranchPrefix = "hotfix";
	private const string MainBranch = "main";
	private const string ReleaseBranchPrefix = "release";
	private const string FeatureBranchPrefix = "feature";

	[Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
	private readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

	[CI] private readonly GitHubActions GitHubActions;
	[Parameter] private readonly string GitHubToken;
	[GitRepository] private readonly GitRepository GitRepository;

	[Required, GitVersion(Framework = "netcoreapp3.1")] private readonly GitVersion GitVersion;

	[Parameter] private readonly bool IgnoreFailedSources;
	[Parameter] private readonly string NuGetApiKey;

	[Solution] private readonly Solution Solution;

	[Partition(2)] private readonly Partition TestPartition;

	#endregion Private Fields

	#region Private Properties

	private string ChangelogFile => RootDirectory / "CHANGELOG.md";
	private IEnumerable<string> ChangelogSectionNotes => ExtractChangelogSectionNotes(ChangelogFile);

	private Target Clean => _ => _
		 .Before(Restore)
		 .Executes(() =>
		 {
			 SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
			 TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
			 EnsureCleanDirectory(OutputDirectory);
		 });

	private Target Compile => _ => _
		 .DependsOn(Restore)
		 .Executes(() => DotNetBuild(_ => _
			 .SetProjectFile(Solution.FileName)
			 .SetNoRestore(InvokedTargets.Contains(Restore))
			 .SetConfiguration(Configuration)
			 .SetRepositoryUrl(GitRepository.HttpsUrl)
			 .SetAssemblyVersion(GitVersion.AssemblySemVer)
			 .SetFileVersion(GitVersion.AssemblySemFileVer)
			 .SetInformationalVersion(GitVersion.InformationalVersion)));

	private Target Coverage => _ => _
		 .DependsOn(Test)
		 .TriggeredBy(Test)
		 .Consumes(Test, CoverageDirectory / "*.xml")
		 .Produces(CoverageReportArchive)
		 .Executes(() =>
		 {
			 ReportGenerator(_ => _
				 .CombineWith(TestProjects, (_, v) => _
					.SetReports(CoverageDirectory / $"{v.Name}/*.xml")
					.SetReportTypes(ReportTypes.HtmlInline, ReportTypes.Badges)
					.SetTargetDirectory(CoverageReportDirectory)
					.SetHistoryDirectory(CoverageReportDirectory / "history")
					.SetFramework("net5.0")
				 ));

			 /*TestsDirectory.GlobFiles("*.xml").ForEach(x =>
				 AzurePipelines?.PublishCodeCoverage(
					 AzurePipelinesCodeCoverageToolType.Cobertura,
					 x,
					 CoverageReportDirectory));*/

			 CompressZip(
				 directory: CoverageReportDirectory,
				 archiveFile: CoverageReportArchive,
				 fileMode: FileMode.Create);
		 });

	private AbsolutePath CoverageDirectory => RootDirectory / "Coverage";

	private AbsolutePath CoverageReportArchive => RootDirectory / "CoverageReport.zip";

	private AbsolutePath CoverageReportDirectory => RootDirectory / "CoverageReport";

	private AbsolutePath DocFxFile => RootDirectory / "docfx.json";

	private string GitHubPackageSource => $"https://nuget.pkg.github.com/{GitHubActions.GitHubRepositoryOwner}/index.json";

	private bool IsOriginalRepository => true;

	private string NuGetPackageSource => "https://api.nuget.org/v3/index.json";

	private AbsolutePath OutputDirectory => RootDirectory / "Output";

	private Target Pack => _ => _
		.DependsOn(Compile)
		.Produces(PackageDirectory / "*.nupkg")
		.Executes(() => DotNetPack(_ => _
			.SetProject(Solution)
			.SetNoBuild(InvokedTargets.Contains(Compile))
			.SetConfiguration(Configuration)
			.SetOutputDirectory(PackageDirectory)
			.SetVersion(GitVersion.NuGetVersionV2)
			.SetPackageReleaseNotes(GetNuGetReleaseNotes(ChangelogFile, GitRepository))));

	private AbsolutePath PackageDirectory => OutputDirectory / "packages";

	private IReadOnlyCollection<AbsolutePath> PackageFiles => PackageDirectory.GlobFiles("*.nupkg");

	private Target Publish => _ => _
		 .ProceedAfterFailure()
		 .DependsOn(Clean, Test, Pack)
		 .Consumes(Pack)
		 .Requires(() => !NuGetApiKey.IsNullOrEmpty() || !IsOriginalRepository)
		 .Requires(() => GitHasCleanWorkingCopy())
		 .Requires(() => Configuration.Equals(Configuration.Release))
		 .Requires(() => (IsOriginalRepository && GitRepository.IsOnMasterBranch()) ||
						 (IsOriginalRepository && GitRepository.IsOnReleaseBranch()) ||
						 (!IsOriginalRepository && GitRepository.IsOnDevelopBranch()))
		 .Executes(() =>
		 {
			 if (!IsOriginalRepository)
			 {
				 DotNetNuGetAddSource(_ => _
					.SetSource(GitHubPackageSource)
					.SetUsername(GitHubActions.GitHubActor)
					.SetPassword(GitHubToken));
			 }
			 DotNetNuGetPush(_ => _
				.SetSource(Source)
				.SetApiKey(NuGetApiKey)
				.CombineWith(PackageFiles, (_, v) => _.SetTargetPath(v)), degreeOfParallelism: 5, completeOnFailure: true);
		 });

	private Target Restore => _ => _.Executes(() => DotNetRestore(s => s.SetProjectFile(Solution)));

	private string Source => IsOriginalRepository ? NuGetPackageSource : GitHubPackageSource;

	private AbsolutePath SourceDirectory => RootDirectory / "Source";

	private Target Test => _ => _
																		 .DependsOn(Compile)
		 .Produces(CoverageDirectory / "*.xml")
		 .Partition(() => TestPartition)
		 .Executes(() => DotNetTest(_ => _
			 .SetConfiguration(Configuration)
			 .SetNoBuild(InvokedTargets.Contains(Compile))
			 .ResetVerbosity()
			 .SetResultsDirectory(CoverageDirectory)
			 .EnableCollectCoverage()
			 .SetCoverletOutputFormat(CoverletOutputFormat.cobertura)
			 .SetExcludeByFile("*.Generated.*")
			 .EnableUseSourceLink()
			 .CombineWith(TestProjects, (_, v) => _
				 .SetProjectFile(v)
				 //.SetLogger($"LogFileName={v.Name}.info")
				 .SetCoverletOutput(CoverageDirectory / $"{v.Name}/cov.xml"))));

	/* GitRepository.Identifier == "nuke-build/nuke";*/
	private IEnumerable<Project> TestProjects => TestPartition.GetCurrent(Solution.GetProjects("*Tests"));

	private AbsolutePath TestsDirectory => RootDirectory / "Tests";

	#endregion Private Properties

	#region Public Methods

	private static void Info(string info)
	{
		Logger.Info(info);
	}

	private static void Info(string info, params object[] args)
	{
		Logger.Info(info, args);
	}

	protected override void OnBuildInitialized()
	{
		Info("\n\nBuilding version {0} of {1} ({2}) using version {3} of Nuke.", GitVersion.NuGetVersion, Solution.Name, Configuration, typeof(NukeBuild).Assembly.GetName().Version.ToString());

		//Information("IsLocalBuild: " + (Host == HostType.Console));
		Info("IsRunningOn: " + Environment.OSVersion.Platform switch
		{
			PlatformID.Unix => "Linux",
			PlatformID.MacOSX => "MacOS",
			_ => "Windows"
		} + ((Host == HostType.Console) ? " (Local)" : " (CI)"));
		Info("Branch: " + GitVersion.BranchName + "\n\n");
	}

	public static int Main() => Execute<Build>(x => x.Compile);

	#endregion Public Methods
}
