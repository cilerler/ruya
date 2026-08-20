[CmdletBinding(DefaultParameterSetName = 'GitRange')]
param(
	[Parameter(Mandatory = $true, ParameterSetName = 'GitRange')]
	[string] $BaseSha,

	[Parameter(ParameterSetName = 'GitRange')]
	[string] $HeadSha = 'HEAD',

	[Parameter(Mandatory = $true, ParameterSetName = 'ChangedPaths')]
	[string[]] $ChangedPath,

	[string] $GitHubOutputPath = $env:GITHUB_OUTPUT
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pathComparer = [System.StringComparer]::OrdinalIgnoreCase

$globalInputs = @(
	'.editorconfig'
	'.github/workflows/Ruya.Package.Pipeline.yml'
	'.github/workflows/dotnet-library-build.yml'
	'CHANGELOG.md'
	'Directory.Build.props'
	'Directory.Build.targets'
	'Directory.Packages.props'
	'global.json'
	'icon.png'
	'README.md'
	'tools/ci/Get-AffectedPackages.ps1'
)

function ConvertTo-PlatformPath {
	param([Parameter(Mandatory = $true)][string] $Path)

	$separator = [IO.Path]::DirectorySeparatorChar.ToString()
	return $Path.Replace('\', $separator).Replace('/', $separator)
}

function ConvertTo-RepositoryPath {
	param([Parameter(Mandatory = $true)][string] $Path)

	return ($Path.Trim().Replace('\', '/') -replace '^\./', '')
}

function Get-RepositoryRelativePath {
	param([Parameter(Mandatory = $true)][string] $FullPath)

	return ([IO.Path]::GetRelativePath($repositoryRoot, $FullPath)).Replace('\', '/')
}

function Get-ChangedPathFromGit {
	param(
		[Parameter(Mandatory = $true)][string] $StartSha,
		[Parameter(Mandatory = $true)][string] $EndSha
	)

	if ($StartSha -match '^0+$') {
		Write-Host 'The push has no usable base commit; treating every package as affected.'
		return @('__all__')
	}

	& git -C $repositoryRoot cat-file -e "$StartSha^{commit}" 2>$null
	if ($LASTEXITCODE -ne 0) {
		Write-Host "Base commit '$StartSha' is unavailable; treating every package as affected."
		return @('__all__')
	}

	& git -C $repositoryRoot cat-file -e "$EndSha^{commit}" 2>$null
	if ($LASTEXITCODE -ne 0) {
		Write-Host "Head commit '$EndSha' is unavailable; treating every package as affected."
		return @('__all__')
	}

	$paths = @(
		& git -C $repositoryRoot -c core.quotepath=false diff --name-only --diff-filter=ACDMRT $StartSha $EndSha --
	)
	if ($LASTEXITCODE -ne 0) {
		Write-Host "The diff between '$StartSha' and '$EndSha' is unavailable; treating every package as affected."
		return @('__all__')
	}

	return $paths
}

function ConvertFrom-WorkflowScalar {
	param([AllowEmptyString()][string] $Value)

	$value = $Value.Trim()
	if ($value.Length -ge 2) {
		if ($value[0] -eq "'" -and $value[-1] -eq "'") {
			return $value.Substring(1, $value.Length - 2).Replace("''", "'")
		}

		if ($value[0] -eq '"' -and $value[-1] -eq '"') {
			return $value.Substring(1, $value.Length - 2)
		}
	}

	return $value
}

function Get-WrapperInputs {
	param([Parameter(Mandatory = $true)][string] $WrapperPath)

	$lines = @(Get-Content -LiteralPath $WrapperPath)
	$withIndex = -1
	for ($index = 0; $index -lt $lines.Count; $index++) {
		if ($lines[$index] -match '^    with:\s*$') {
			$withIndex = $index
			break
		}
	}

	if ($withIndex -lt 0) {
		throw "Package wrapper '$WrapperPath' has no build with block."
	}

	$inputs = @{}
	$index = $withIndex + 1
	while ($index -lt $lines.Count) {
		$line = $lines[$index]
		if ($line -match '^    \S') {
			break
		}

		if ($line -notmatch '^      (?<key>[a-z0-9-]+):(?:\s*(?<value>.*))?$') {
			$index++
			continue
		}

		$key = $Matches.key
		$value = $Matches.value
		if ($value -match '^[|>]') {
			$blockLines = [System.Collections.Generic.List[string]]::new()
			$index++
			while ($index -lt $lines.Count) {
				$blockLine = $lines[$index]
				if ($blockLine -match '^        (?<content>.*)$') {
					$blockLines.Add($Matches.content)
					$index++
					continue
				}

				if ([string]::IsNullOrWhiteSpace($blockLine)) {
					$blockLines.Add('')
					$index++
					continue
				}

				break
			}

			$inputs[$key] = ($blockLines -join "`n").Trim()
			continue
		}

		$inputs[$key] = ConvertFrom-WorkflowScalar -Value $value
		$index++
	}

	return $inputs
}

function Get-BooleanInput {
	param(
		[Parameter(Mandatory = $true)][hashtable] $Inputs,
		[Parameter(Mandatory = $true)][string] $Name,
		[bool] $Default = $false
	)

	if (-not $Inputs.ContainsKey($Name)) {
		return $Default
	}

	$value = $false
	if (-not [bool]::TryParse([string] $Inputs[$Name], [ref] $value)) {
		throw "Workflow input '$Name' must be a literal boolean, but was '$($Inputs[$Name])'."
	}

	return $value
}

function Get-XmlProjectReferences {
	param([Parameter(Mandatory = $true)][xml] $ProjectXml)

	return @(
		foreach ($projectReference in @($ProjectXml.SelectNodes('/Project/ItemGroup/ProjectReference'))) {
			if ($projectReference.Include) {
				[string] $projectReference.Include
			}
		}
	)
}

function Get-XmlRuyaPackageReferences {
	param([Parameter(Mandatory = $true)][xml] $ProjectXml)

	return @(
		foreach ($packageReference in @($ProjectXml.SelectNodes('/Project/ItemGroup/PackageReference'))) {
			if ([string] $packageReference.Include -like 'Ruya.*') {
				[string] $packageReference.Include
			}
		}
	)
}

function Get-EvaluatedReleaseProject {
	param([Parameter(Mandatory = $true)][string] $ProjectPath)

	$output = @(
		& dotnet msbuild $ProjectPath `
			--nologo `
			-p:Configuration=Release `
			-getProperty:PackageId,IsPackable `
			-getItem:PackageReference,ProjectReference
	)
	if ($LASTEXITCODE -ne 0) {
		throw "MSBuild evaluation failed for '$ProjectPath'."
	}

	$json = ($output -join "`n").Trim()
	try {
		return $json | ConvertFrom-Json
	}
	catch {
		throw "MSBuild returned invalid evaluation JSON for '$ProjectPath': $($_.Exception.Message)"
	}
}

$packageProjects = [System.Collections.Generic.Dictionary[string, string]]::new($pathComparer)
$packageByProjectPath = [System.Collections.Generic.Dictionary[string, string]]::new($pathComparer)
$evaluatedProjects = [System.Collections.Generic.Dictionary[string, object]]::new($pathComparer)
foreach ($projectFile in @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -Filter '*.csproj' -File | Sort-Object FullName)) {
	$fullPath = [IO.Path]::GetFullPath($projectFile.FullName)
	$evaluation = Get-EvaluatedReleaseProject -ProjectPath $fullPath
	$isPackable = $true
	if ($null -ne $evaluation.Properties.PSObject.Properties['IsPackable'] -and
		-not [string]::IsNullOrWhiteSpace($evaluation.Properties.IsPackable)) {
		if (-not [bool]::TryParse([string] $evaluation.Properties.IsPackable, [ref] $isPackable)) {
			throw "Project '$fullPath' returned invalid IsPackable value '$($evaluation.Properties.IsPackable)'."
		}
	}

	if (-not $isPackable) {
		continue
	}

	$package = [string] $evaluation.Properties.PackageId
	if ([string]::IsNullOrWhiteSpace($package)) {
		throw "Packable project '$fullPath' returned no PackageId."
	}

	if ($packageProjects.ContainsKey($package)) {
		throw "Duplicate PackageId '$package' was discovered."
	}

	$packageProjects.Add($package, $fullPath)
	$packageByProjectPath.Add($fullPath, $package)
	$evaluatedProjects.Add($package, $evaluation)
}

if ($packageProjects.Count -eq 0) {
	throw 'No package projects were discovered under src/.'
}

$packageNames = @($packageProjects.Keys | Sort-Object)
$sourceDirectoryPackages = [System.Collections.Generic.Dictionary[string, string]]::new($pathComparer)
foreach ($package in $packageNames) {
	$sourceDirectory = Get-RepositoryRelativePath -FullPath ([IO.Path]::GetDirectoryName($packageProjects[$package]))
	if ($sourceDirectoryPackages.ContainsKey($sourceDirectory)) {
		throw "Source directory '$sourceDirectory' contains more than one packable project."
	}

	$sourceDirectoryPackages.Add($sourceDirectory, $package)
}

$wrapperMetadata = [System.Collections.Generic.Dictionary[string, object]]::new($pathComparer)
foreach ($package in $packageNames) {
	$wrapperPath = Join-Path $repositoryRoot ".github/workflows/$package.yml"
	if (-not (Test-Path -LiteralPath $wrapperPath -PathType Leaf)) {
		throw "Package '$package' has no workflow wrapper at '.github/workflows/$package.yml'."
	}

	$inputs = Get-WrapperInputs -WrapperPath $wrapperPath
	foreach ($requiredInput in @('project-name', 'project-path')) {
		if (-not $inputs.ContainsKey($requiredInput) -or [string]::IsNullOrWhiteSpace($inputs[$requiredInput])) {
			throw "Package wrapper '$wrapperPath' is missing '$requiredInput'."
		}
	}

	if ($inputs['project-name'] -ne $package) {
		throw "Package wrapper '$wrapperPath' declares project '$($inputs['project-name'])' instead of '$package'."
	}

	$declaredProjectPath = [IO.Path]::GetFullPath(
		(Join-Path $repositoryRoot (ConvertTo-PlatformPath -Path $inputs['project-path']))
	)
	if (-not $pathComparer.Equals($declaredProjectPath, $packageProjects[$package])) {
		throw "Package wrapper '$wrapperPath' points to '$declaredProjectPath' instead of '$($packageProjects[$package])'."
	}

	$wrapperMetadata.Add($package, [pscustomobject]@{
		ProjectName = $package
		ProjectPath = [string] $inputs['project-path']
		TestProjectPaths = if ($inputs.ContainsKey('test-project-paths')) { [string] $inputs['test-project-paths'] } else { '' }
		TestConfiguration = if ($inputs.ContainsKey('test-configuration')) { [string] $inputs['test-configuration'] } else { 'Release' }
		UseTestcontainers = Get-BooleanInput -Inputs $inputs -Name 'use-testcontainers'
		UseLocalTokenBrokerProjectReferences = Get-BooleanInput -Inputs $inputs -Name 'use-local-tokenbroker-project-references'
		TestcontainersTimeout = if ($inputs.ContainsKey('testcontainers-timeout')) { [string] $inputs['testcontainers-timeout'] } else { '300s' }
	})
}

$packageDependencies = [System.Collections.Generic.Dictionary[string, object]]::new($pathComparer)
$packageConsumers = [System.Collections.Generic.Dictionary[string, object]]::new($pathComparer)
foreach ($package in $packageNames) {
	$packageDependencies.Add($package, [System.Collections.Generic.HashSet[string]]::new($pathComparer))
	$packageConsumers.Add($package, [System.Collections.Generic.HashSet[string]]::new($pathComparer))
}

foreach ($package in $packageNames) {
	$evaluation = $evaluatedProjects[$package]
	$projectReferenceProperty = $evaluation.Items.PSObject.Properties['ProjectReference']
	if ($null -ne $projectReferenceProperty) {
		foreach ($reference in @($projectReferenceProperty.Value)) {
			$referencePath = if (-not [string]::IsNullOrWhiteSpace($reference.FullPath)) {
				[IO.Path]::GetFullPath((ConvertTo-PlatformPath -Path $reference.FullPath))
			}
			else {
				[IO.Path]::GetFullPath(
					(Join-Path ([IO.Path]::GetDirectoryName($packageProjects[$package])) (ConvertTo-PlatformPath -Path $reference.Identity))
				)
			}

			if ($packageByProjectPath.ContainsKey($referencePath)) {
				[void] $packageDependencies[$package].Add($packageByProjectPath[$referencePath])
			}
		}
	}

	$packageReferenceProperty = $evaluation.Items.PSObject.Properties['PackageReference']
	if ($null -ne $packageReferenceProperty) {
		foreach ($reference in @($packageReferenceProperty.Value)) {
			$identity = [string] $reference.Identity
			if ($identity -like 'Ruya.*' -and -not $packageProjects.ContainsKey($identity)) {
				throw "Package '$package' has evaluated Release dependency '$identity', but no packable source project with that PackageId was discovered."
			}

			if ($identity -like 'Ruya.*') {
				[void] $packageDependencies[$package].Add($identity)
			}
		}
	}

	[void] $packageDependencies[$package].Remove($package)
}

foreach ($consumer in $packageNames) {
	foreach ($dependency in $packageDependencies[$consumer]) {
		[void] $packageConsumers[$dependency].Add($consumer)
	}
}

$depths = [System.Collections.Generic.Dictionary[string, int]]::new($pathComparer)
$visitState = [System.Collections.Generic.Dictionary[string, int]]::new($pathComparer)
$visitStack = [System.Collections.Generic.List[string]]::new()

function Resolve-PackageDepth {
	param([Parameter(Mandatory = $true)][string] $Package)

	if ($depths.ContainsKey($Package)) {
		return $depths[$Package]
	}

	if ($visitState.ContainsKey($Package) -and $visitState[$Package] -eq 1) {
		$cycle = @($visitStack) + $Package
		throw "Package dependency cycle detected: $($cycle -join ' -> ')"
	}

	$visitState[$Package] = 1
	$visitStack.Add($Package)
	try {
		$depth = 0
		foreach ($dependency in $packageDependencies[$Package]) {
			$dependencyDepth = Resolve-PackageDepth -Package $dependency
			$depth = [Math]::Max($depth, $dependencyDepth + 1)
		}

		if ($depth -gt 2) {
			throw "Package '$Package' resolves to dependency level $depth. The pipeline supports levels 0 through 2; add another layer before merging this dependency."
		}

		$depths.Add($Package, $depth)
		$visitState[$Package] = 2
		return $depth
	}
	finally {
		$visitStack.RemoveAt($visitStack.Count - 1)
	}
}

foreach ($package in $packageNames) {
	[void] (Resolve-PackageDepth -Package $package)
}

$testConsumers = [System.Collections.Generic.Dictionary[string, object]]::new($pathComparer)
$testWatchOwners = [System.Collections.Generic.Dictionary[string, object]]::new($pathComparer)
foreach ($package in $packageNames) {
	$testConsumers.Add($package, [System.Collections.Generic.HashSet[string]]::new($pathComparer))
}

function Visit-OwnedTestProject {
	param(
		[Parameter(Mandatory = $true)][string] $Owner,
		[Parameter(Mandatory = $true)][string] $ProjectPath,
		[Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]] $Visited,
		[Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]] $SourceDependencies
	)

	$projectPath = [IO.Path]::GetFullPath($ProjectPath)
	if (-not $Visited.Add($projectPath)) {
		return
	}

	if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
		throw "Test project '$projectPath' configured by '$Owner' does not exist."
	}

	$relativeProjectPath = Get-RepositoryRelativePath -FullPath $projectPath
	if ($relativeProjectPath.StartsWith('tests/', [System.StringComparison]::OrdinalIgnoreCase)) {
		$watchPath = ([IO.Path]::GetDirectoryName($relativeProjectPath)).Replace('\', '/')
		if (-not $testWatchOwners.ContainsKey($watchPath)) {
			$testWatchOwners.Add($watchPath, [System.Collections.Generic.HashSet[string]]::new($pathComparer))
		}

		[void] $testWatchOwners[$watchPath].Add($Owner)
	}

	[xml] $projectXml = Get-Content -Raw -LiteralPath $projectPath
	foreach ($reference in @(Get-XmlProjectReferences -ProjectXml $projectXml)) {
		$referencePath = [IO.Path]::GetFullPath(
			(Join-Path ([IO.Path]::GetDirectoryName($projectPath)) (ConvertTo-PlatformPath -Path $reference))
		)
		if ($packageByProjectPath.ContainsKey($referencePath)) {
			$dependency = $packageByProjectPath[$referencePath]
			if ($dependency -ne $Owner) {
				[void] $SourceDependencies.Add($dependency)
			}
			continue
		}

		if (Test-Path -LiteralPath $referencePath -PathType Leaf) {
			Visit-OwnedTestProject -Owner $Owner -ProjectPath $referencePath -Visited $Visited -SourceDependencies $SourceDependencies
		}
	}

	foreach ($reference in @(Get-XmlRuyaPackageReferences -ProjectXml $projectXml)) {
		if ($reference -ne $Owner -and $packageProjects.ContainsKey($reference)) {
			[void] $SourceDependencies.Add($reference)
		}
	}
}

foreach ($owner in $packageNames) {
	$testProjectPaths = @(
		$wrapperMetadata[$owner].TestProjectPaths -split '[,\r\n]+' |
			ForEach-Object { $_.Trim() } |
			Where-Object { $_ }
	)
	if ($testProjectPaths.Count -eq 0) {
		continue
	}

	$visited = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
	$sourceDependencies = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
	foreach ($configuredPath in $testProjectPaths) {
		$platformPath = ConvertTo-PlatformPath -Path $configuredPath
		$resolvedProjects = @()
		if ([System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($platformPath)) {
			$resolvedProjects = @(Get-ChildItem -Path (Join-Path $repositoryRoot $platformPath) -File)
		}
		else {
			$resolvedProjects = @(Get-Item -LiteralPath (Join-Path $repositoryRoot $platformPath))
		}

		if ($resolvedProjects.Count -eq 0) {
			throw "Test project pattern '$configuredPath' configured by '$owner' matched no files."
		}

		foreach ($testProject in $resolvedProjects) {
			Visit-OwnedTestProject -Owner $owner -ProjectPath $testProject.FullName -Visited $visited -SourceDependencies $sourceDependencies
		}
	}

	foreach ($sourceDependency in $sourceDependencies) {
		[void] $testConsumers[$sourceDependency].Add($owner)
	}
}

if ($PSCmdlet.ParameterSetName -eq 'GitRange') {
	$changedPaths = @(Get-ChangedPathFromGit -StartSha $BaseSha -EndSha $HeadSha)
}
else {
	$changedPaths = @($ChangedPath)
}

$changedPaths = @(
	$changedPaths |
		ForEach-Object { ConvertTo-RepositoryPath -Path $_ } |
		Where-Object { $_ }
)

$productionChanged = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
$selectedOnly = [System.Collections.Generic.HashSet[string]]::new($pathComparer)

if ($changedPaths -contains '__all__' -or @($changedPaths | Where-Object { $globalInputs -contains $_ }).Count -gt 0) {
	$productionChanged.UnionWith([string[]] $packageNames)
}
else {
	foreach ($path in $changedPaths) {
		foreach ($sourceDirectory in $sourceDirectoryPackages.Keys) {
			if ($path.Equals($sourceDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
				$path.StartsWith("$sourceDirectory/", [System.StringComparison]::OrdinalIgnoreCase)) {
				[void] $productionChanged.Add($sourceDirectoryPackages[$sourceDirectory])
			}
		}

		if ($path -match '^\.github/workflows/(Ruya\..+)\.yml$') {
			$workflowPackage = $Matches[1]
			if ($packageProjects.ContainsKey($workflowPackage)) {
				[void] $selectedOnly.Add($workflowPackage)
			}
		}

		foreach ($watchPath in $testWatchOwners.Keys) {
			if ($path.Equals($watchPath, [System.StringComparison]::OrdinalIgnoreCase) -or
				$path.StartsWith("$watchPath/", [System.StringComparison]::OrdinalIgnoreCase)) {
				$selectedOnly.UnionWith($testWatchOwners[$watchPath])
			}
		}
	}
}

$affected = [System.Collections.Generic.HashSet[string]]::new($productionChanged, $pathComparer)
$affected.UnionWith($selectedOnly)
$productionAffected = [System.Collections.Generic.HashSet[string]]::new($productionChanged, $pathComparer)
$pending = [System.Collections.Generic.Queue[string]]::new()
foreach ($package in $productionChanged) {
	$pending.Enqueue($package)
}

while ($pending.Count -gt 0) {
	$dependency = $pending.Dequeue()
	foreach ($consumer in $packageConsumers[$dependency]) {
		[void] $affected.Add($consumer)
		if ($productionAffected.Add($consumer)) {
			$pending.Enqueue($consumer)
		}
	}

	$affected.UnionWith($testConsumers[$dependency])
}

function New-MatrixEntry {
	param([Parameter(Mandatory = $true)][string] $Package)

	$metadata = $wrapperMetadata[$Package]
	return [ordered]@{
		project_name = $metadata.ProjectName
		project_path = $metadata.ProjectPath
		test_project_paths = $metadata.TestProjectPaths
		test_configuration = $metadata.TestConfiguration
		use_testcontainers = $metadata.UseTestcontainers
		use_local_tokenbroker_project_references = $metadata.UseLocalTokenBrokerProjectReferences
		testcontainers_timeout = $metadata.TestcontainersTimeout
	}
}

$layerMatrices = @{}
for ($level = 0; $level -le 2; $level++) {
	$include = @(
		$packageNames |
			Where-Object { $affected.Contains($_) -and $depths[$_] -eq $level } |
			ForEach-Object { New-MatrixEntry -Package $_ }
	)
	$layerMatrices[$level] = [ordered]@{ include = $include }
}

$orderedAffected = @($packageNames | Where-Object { $affected.Contains($_) })
$packagesJson = ConvertTo-Json -InputObject $orderedAffected -Compress
if ($orderedAffected.Count -eq 0) {
	$packagesJson = '[]'
}

Write-Host "Discovered package levels: $(@($packageNames | ForEach-Object { "$_=$($depths[$_])" }) -join ', ')"
Write-Host "Changed paths ($($changedPaths.Count)): $($changedPaths -join ', ')"
Write-Host "Production source packages changed ($($productionChanged.Count)): $(@($packageNames | Where-Object { $productionChanged.Contains($_) }) -join ', ')"
Write-Host "Wrapper/test-only packages selected ($($selectedOnly.Count)): $(@($packageNames | Where-Object { $selectedOnly.Contains($_) }) -join ', ')"
Write-Host "Affected packages ($($orderedAffected.Count)): $($orderedAffected -join ', ')"

if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
	Add-Content -LiteralPath $GitHubOutputPath -Value "packages=$packagesJson" -Encoding utf8
	Add-Content -LiteralPath $GitHubOutputPath -Value "count=$($orderedAffected.Count)" -Encoding utf8
	for ($level = 0; $level -le 2; $level++) {
		$matrixJson = ConvertTo-Json -InputObject $layerMatrices[$level] -Depth 10 -Compress
		$layerCount = @($layerMatrices[$level].include).Count
		Add-Content -LiteralPath $GitHubOutputPath -Value "layer_$level=$matrixJson" -Encoding utf8
		Add-Content -LiteralPath $GitHubOutputPath -Value "layer_${level}_count=$layerCount" -Encoding utf8
	}
}

[ordered]@{
	packages = $orderedAffected
	layer_0 = $layerMatrices[0]
	layer_1 = $layerMatrices[1]
	layer_2 = $layerMatrices[2]
} | ConvertTo-Json -Depth 10 -Compress
