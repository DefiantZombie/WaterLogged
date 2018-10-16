#tool "nuget:?package=GitVersion.CommandLine&version=3.6.5"
#tool "nuget:?package=gitreleasemanager"
#tool "nuget:?package=vswhere"
#addin "nuget:?package=Cake.Git&version=0.18.0"
#addin "nuget:?package=SharpZipLib"
#addin "nuget:?package=Cake.Compression"
#addin "nuget:?package=Newtonsoft.Json"
//#addin "Cake.FileHelpers"
#addin "Cake.Incubator"
#addin "Cake.Http"
using Cake.Incubator;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var devbuild = bool.Parse(Argument("devbuild", "False"));
var step = Argument("step", "").ToLower();

var solutionFile = GetFiles("./*.sln").First();
var solution = ParseSolution(solutionFile);

var isLocalBuild = !Jenkins.IsRunningOnJenkins;

 // TODO: Get rid of this
var nugetLocal = EnvironmentVariable("NUGETLOCAL");
if(nugetLocal is null)
{
	throw new Exception("Environment variable NUGETLOCAL not found");
}

var versionInfo = DirectoryExists(".git") ? GitVersion() : new GitVersion();

var projectName = "Unnamed Project";

public struct GameVersion
{
	public int id;
	public int gameVersionTypeID;
	public string name;
	public string slug;
}

public struct CurseMetadata
{
	public string changelog; // Can be HTML or markdown if changelogType set
	public string changelogType; // text, html, markdown
	//public string displayName; // optional
	//public int parentFileID; // optional
	public int[] gameVersions; // list of versions from Curse version scheme
	public string releaseType; // alpha, beta, release
}

public byte[] FileToByteArray(string filename)
{
	byte[] fileData = null;

	using(var fs = System.IO.File.OpenRead(filename))
	{
		using(var br = new BinaryReader(fs))
		{
			fileData = br.ReadBytes((int)fs.Length);
		}
	}

	return fileData;
}

public Dictionary<string, int> GetCurseGameVersions()
{
	var settings = new HttpSettings
	{
		Headers = new Dictionary<string, string>
		{
			{ "X-Api-Token", $"{EnvironmentVariable("CURSE_TOKEN")}" }
		}
	};

	var response = HttpGet("https://kerbal.curseforge.com/api/game/versions", settings);

	var versionJson = JsonConvert.DeserializeObject<List<GameVersion>>(response);

	var dict = new Dictionary<string, int>();

	foreach(var v in versionJson)
	{
		dict.Add(v.name, v.id);
	}

	return dict;
}

public static async Task<byte[]> FormToByteArray(MultipartFormDataContent content)
{
	return await content.ReadAsByteArrayAsync();
}

public static async Task<string> FormToString(MultipartFormDataContent content)
{
	return await content.ReadAsStringAsync();
}

public string CreateCurseMetadata(Dictionary<string, int> gameVersionData)
{
	// TODO: Add changelog and release type logic

	var versionStrings = EnvironmentVariable("GAME_VERSIONS").Split(',');

	var metadata = new CurseMetadata
	{
		changelog = "",
		changelogType = "markdown",
		gameVersions = versionStrings.Select(v => gameVersionData[v]).ToArray(),
		releaseType = "release"
	};

	return JsonConvert.SerializeObject(metadata);
}

Setup(ctx =>
{
	if(isLocalBuild && !ctx.Arguments.HasArgument("configuration"))
	{
		configuration = "Debug";
	}

	projectName = HasEnvironmentVariable("PROJECT_NAME")
		? EnvironmentVariable("PROJECT_NAME")
		: ctx.Environment.WorkingDirectory.GetDirectoryName();
});

Task("Clean")
	.Description("Clean up build and release paths.")
	.WithCriteria(isLocalBuild || step == "build")
	.Does(() =>
	{
		CleanDirectories("./**/obj/**");
		CleanDirectories("./**/build");
		CleanDirectories("./artifacts");
	});

Task("Restore")
	.Description("Restore NuGet packages. Adds local source if missing.")
	.WithCriteria(isLocalBuild || step == "build")
	.Does(() =>
	{
		if(!NuGetHasSource(nugetLocal))
		{
			NuGetAddSource("Local", nugetLocal);
		}

		NuGetRestore(solutionFile);
	});

Task("Version")
	.Description("Update version in AssemblyInfo file(s).")
	.WithCriteria(isLocalBuild || step == "build")
	.Does(() =>
	{
		GitVersion(new GitVersionSettings {
			UpdateAssemblyInfo = true
		});

		// TODO: Update any *.version files
	});

Task("Build")
	.Description("Run the build.")
	.WithCriteria(isLocalBuild || step == "build")
	.IsDependentOn("Clean")
	.IsDependentOn("Restore")
	.IsDependentOn("Version")
	.Does(() =>
	{
		var msBuildPath = VSWhereLatest().CombineWithFilePath("./MSBuild/15.0/Bin/amd64/MSBuild.exe");

		var settings = new MSBuildSettings {
			ToolPath = msBuildPath,
			Configuration = configuration,
			Verbosity = Verbosity.Minimal
		};
		settings.WithTarget("Rebuild");

		MSBuild(solutionFile, settings);
	});

Task("Package")
	.Description("Pack up the build for release.")
	.WithCriteria(isLocalBuild || step == "package")
	.IsDependentOn("Build")
	.Does(() =>
	{
		var tempPath = new DirectoryPath($"./artifacts/tmp/Squidsoft Collective/{projectName}");

		EnsureDirectoryExists(tempPath);

		// Copy assemblies
		var files = GetFiles("./**/*.csproj");
		foreach(var file in files)
		{
			var project = ParseProject(file);
			var references = project.References.Where(r => r.Private != false && r.HintPath != null
				&& !string.IsNullOrWhiteSpace(r.HintPath.ToString()))
				.Select(r => r.HintPath.GetFilename());

			var assemblyPaths = GetProjectAssemblies(file, configuration);
			var buildPath = assemblyPaths.First().GetDirectory();
			var assemblies = assemblyPaths.Select(a => a.GetFilename());

			var xferFiles = new List<FilePath>();
			foreach(var r in references)
			{
				xferFiles.Add(buildPath.CombineWithFilePath(r));
			}
			CopyFiles(xferFiles, tempPath.Combine(".."));

			xferFiles = new List<FilePath>();
			foreach(var a in assemblies)
			{
				xferFiles.Add(buildPath.CombineWithFilePath(a));
			}
			CopyFiles(xferFiles, tempPath);
		}

		// Copy project files
		var dirs = GetDirectories("./**/ProjectFiles");
		foreach(var d in dirs)
		{
			CopyDirectory(d, tempPath);
		}

		// Compress
		var kspVer = EnvironmentVariable("KSPVER");
		ZipCompress("./artifacts/tmp", $"./artifacts/{projectName}-ksp{kspVer}-{versionInfo.SemVer}.zip", 9);

		// Cleanup
		DeleteDirectory("./artifacts/tmp", new DeleteDirectorySettings {
			Recursive = true
			});
	});

Task("Release-Github")
	.Description("Push the build to Github.")
	.WithCriteria(isLocalBuild || step == "release-github")
	.IsDependentOn("Package")
	.Does(() =>
	{
		var githubUser = EnvironmentVariable("GITHUB_CRED_USR");
		var githubPass = EnvironmentVariable("GITHUB_CRED_PSW");
		var repo = EnvironmentVariable("PROJECT_NAME");

		var files = GetFiles("./artifacts/*").Select(f => f.ToString());

		GitReleaseManagerCreateSettings settings;
		if(devbuild)
		{
			settings = new GitReleaseManagerCreateSettings
			{
				Name = versionInfo.SemVer,
				Prerelease = true,
				InputFilePath = "ReleaseNotes.md",
				Assets = string.Join(",", files),
				TargetCommitish = GitBranchCurrent(".").FriendlyName
			};
		}
		else
		{
			settings = new GitReleaseManagerCreateSettings
			{
				Milestone = versionInfo.SemVer,
				Prerelease = false,
				Assets = string.Join(",", files),
				TargetCommitish = "master"
			};
		}

		GitReleaseManagerCreate(githubUser, githubPass, githubUser, repo, settings);

		GitReleaseManagerPublish(githubUser, githubPass, githubUser, repo, versionInfo.SemVer);
	});

Task("Release-Curse")
	.Description("Push the build to Curse.")
	.WithCriteria(isLocalBuild || step == "release-curse")
	.Does(() =>
	{
		var gameVersions = GetCurseGameVersions();

		var files = GetFiles("./artifacts/*.zip");

		var packs = new List<byte[]>();

		foreach(var f in files)
		{
			using(var mpfd = new MultipartFormDataContent())
			{
				mpfd.Add(new StringContent(CreateCurseMetadata(gameVersions)), "metadata");
				mpfd.Add(new ByteArrayContent(FileToByteArray(f.ToString())), "file");

				packs.Add(FormToByteArray(mpfd).Result);
			}
		}

		var settings = new HttpSettings
		{
			Headers = new Dictionary<string, string>
			{
				{ "X-Api-Token", $"{EnvironmentVariable("CURSE_TOKEN")}" }
			}
		};

		var postUrl = $"https://kerbal.curseforge.com/api/projects/{EnvironmentVariable("CURSE_ID")}/upload-file";
		foreach(var p in packs)
		{
			settings.RequestBody = p;

			HttpPost(postUrl, settings);
		}

		// https://kerbal.curseforge.com/
	});

Task("Cleanup")
	.WithCriteria(!GitBranchCurrent(".").FriendlyName.StartsWith("release"))
	.Does(() => {
		var files = GetFiles("**/AssemblyInfo.cs").ToArray();

		if(files.Length > 0)
		{
			GitCheckout(".", files);
		}
	});

Task("Post-build")
	.Description("Runs a post-build script if one exists.")
	.WithCriteria(isLocalBuild && FileExists("./post.cake"))
	.Does(() =>
	{
		CakeExecuteScript("./post.cake", new CakeSettings {
			Arguments = new Dictionary<string, string> {
				{ "target", target },
				{ "configuration", configuration },
				{ "devbuild", Argument("devbuild", "False") },
				{ "step", step }
			}
		});
	});

Task("Notify-Twitter")
	.Description("Send a tweet about the new live release.")
	.WithCriteria(isLocalBuild || step == "notify-twitter")
	.Does(() => {
		Information("Not implimented");
	});

Task("Notify-Discord")
	.Description("Send a Discord message about the new live release.")
	.WithCriteria(isLocalBuild || step == "notify-discord")
	.Does(() => {
		Information("Not implimented");
	});

Task("ContinuousIntegration")
	.Description("Run the selected CI step.")
	.WithCriteria(!isLocalBuild)
	.IsDependentOn("Build")
	.IsDependentOn("Package")
	.IsDependentOn("Release-Github")
	.IsDependentOn("Release-Curse")
	.IsDependentOn("Notify-Twitter")
	.IsDependentOn("Notify-Discord")
	.Does(() => {
	});

Task("Default")
	.IsDependentOn("Build")
	.IsDependentOn("Post-build")
	.IsDependentOn("Package")
	.IsDependentOn("Cleanup")
	.Does(() => {
	});

RunTarget(target);
