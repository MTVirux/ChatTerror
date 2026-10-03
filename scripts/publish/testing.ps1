param(
    [string]$CustomVersion
)

# Get the latest testing tag from the repository
git fetch --tags

# Check if local branch is up to date with remote
$currentBranch = git rev-parse --abbrev-ref HEAD
$localCommit = git rev-parse "@"
$remoteCommit = git rev-parse "@{u}"

if ($localCommit -ne $remoteCommit) {
    Write-Error "Local branch '$currentBranch' is not up to date with remote. Please pull the latest changes before publishing."
    exit 1
}

Write-Host "Local branch is up to date with remote."

if ($CustomVersion) {
    # Use the provided custom version
    $version = $CustomVersion
    $newTag = "testing_$version"
} else {
    $latestTag = git tag -l "testing_*" | ForEach-Object {
        $version = $_ -replace '^testing_', ''
        [PSCustomObject]@{
            Tag = $_
            Version = [Version]$version
        }
    } | Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty Tag

    if (-not $latestTag) {
        Write-Host "No existing testing tags found. Creating initial tag testing_1.0.0.0"
        $newTag = "testing_1.0.0.0"
        $version = "1.0.0.0"
    } else {
        Write-Host "Latest testing tag: $latestTag"

        # Remove the "testing_" prefix to get the version
        $version = $latestTag -replace '^testing_', ''

        # Split the version by periods
        $parts = $version -split '\.'

        # Increment the last portion
        $lastIndex = $parts.Length - 1
        $parts[$lastIndex] = [int]$parts[$lastIndex] + 1

        # Join back together
        $version = $parts -join '.'
        $newTag = "testing_$version"
    }
}

Write-Host "New testing tag: $newTag"
Write-Host "Version: $version"

# Get the repository root (parent of scripts folder)
$scriptDir = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptDir

# Auto-detect project files. Mirrors the discovery in build/*.ps1 so the tag,
# csproj versions, bin-manifest stub and repo.json all land in one commit.
Write-Host "Auto-detecting project files..."
$submoduleAbs = @()
$gitmodulesPath = Join-Path $repoRoot '.gitmodules'
if (Test-Path $gitmodulesPath) {
    $gm = Get-Content $gitmodulesPath -ErrorAction SilentlyContinue
    foreach ($line in $gm) {
        if ($line -match '^\s*path\s*=\s*(.+)$') {
            $p = $matches[1].Trim()
            $abs = (Join-Path $repoRoot $p).Replace('/','\\')
            $submoduleAbs += $abs
        }
    }
}

$csprojCandidates = Get-ChildItem -Path $repoRoot -Filter *.csproj -Recurse -ErrorAction SilentlyContinue
$csprojFile = $csprojCandidates | Where-Object {
    $full = $_.FullName
    if ($full -match '\\(bin|obj)\\') { return $false }
    if ($full -match '\\\.claude\\') { return $false }  # ignore agent worktrees under .claude/
    foreach ($sm in $submoduleAbs) { if ($full.StartsWith($sm, [System.StringComparison]::InvariantCultureIgnoreCase)) { return $false } }
    return $true
} | Select-Object -First 1
if (-not $csprojFile) {
    Write-Error "No .csproj file found in repository."
    exit 1
}
$csprojPath = $csprojFile.FullName
$ProjectDirFull = $csprojFile.Directory.FullName
Write-Host "Using csproj: $csprojPath"

$JsonName = $null
$projJsons = Get-ChildItem -Path $ProjectDirFull -Filter *.json -ErrorAction SilentlyContinue
foreach ($j in $projJsons) {
    $content = Get-Content $j.FullName -Raw -ErrorAction SilentlyContinue
    if ($content -and $content -match 'AssemblyVersion') { $JsonName = $j.Name; break }
}
if (-not $JsonName) {
    $rootJsons = Get-ChildItem -Path $repoRoot -Filter *.json -ErrorAction SilentlyContinue
    foreach ($j in $rootJsons) {
        if ($j.Name -ieq 'repo.json') { continue }
        $content = Get-Content $j.FullName -Raw -ErrorAction SilentlyContinue
        if ($content -and $content -match 'AssemblyVersion') { $JsonName = $j.Name; break }
    }
}
if (-not $JsonName) {
    Write-Error "Could not find a project manifest JSON containing AssemblyVersion."
    exit 1
}
$projectJsonPath = if (Test-Path (Join-Path $ProjectDirFull $JsonName)) { Join-Path $ProjectDirFull $JsonName } else { Join-Path $repoRoot $JsonName }
Write-Host "Using project json: $projectJsonPath"

# Update csproj versions (only the tags that are actually present)
Write-Host "Updating csproj versions..."
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$version</FileVersion>"
$csproj = $csproj -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$version</AssemblyVersion>"
$csproj = $csproj -replace '<Version>[\d\.]+</Version>', "<Version>$version</Version>"
Set-Content -Path $csprojPath -Value $csproj -NoNewline

# Update project manifest JSON (the bin-manifest stub copied to bin/<config>/).
# CI rewrites this file from repo.json[0] at release time, but committing the
# updated AssemblyVersion keeps in-tree state consistent with the tag.
Write-Host "Updating project manifest AssemblyVersion..."
$projectJson = Get-Content $projectJsonPath -Raw | ConvertFrom-Json
$projectJson.AssemblyVersion = $version
$projectJson | ConvertTo-Json -Depth 10 | Set-Content -Path $projectJsonPath

# Update repo.json
Write-Host "Updating repo.json..."
$repoJsonPath = Join-Path $repoRoot "repo.json"
$repoJsonRaw = Get-Content $repoJsonPath -Raw
$repoJson = $repoJsonRaw | ConvertFrom-Json
if ($repoJson -isnot [System.Collections.IEnumerable] -or $repoJson -is [string]) {
    $repoJson = @($repoJson)
}
$timestamp = [int][double]::Parse((Get-Date -UFormat %s))
$repoJson[0].TestingAssemblyVersion = $version
$repoJson[0].LastUpdate = $timestamp
$repoJsonJson = $repoJson | ConvertTo-Json -Depth 10
$trimmed = $repoJsonJson.Trim()
$nl = [Environment]::NewLine
if ($trimmed.StartsWith('{')) {
    $repoJsonJson = '[' + $nl + $repoJsonJson + $nl + ']'
}
Set-Content -Path $repoJsonPath -Value $repoJsonJson

# Stage the version-bearing files together
git add $csprojPath $projectJsonPath $repoJsonPath

$stagedChanges = git diff --cached --name-only
if ($stagedChanges) {
    Write-Host "Committing version changes..."
    git commit -m "[SCRIPT] Update testing version to $version in repo and bin manifest"

    # Push the commit first
    Write-Host "Pushing version changes to $currentBranch..."
    git push origin $currentBranch

    # Verify the commit is on remote with retry logic
    Write-Host "Verifying commit on remote..."
    $maxAttempts = 90  # 3 minutes at 2 seconds per attempt
    $attempt = 0
    $verified = $false

    while ($attempt -lt $maxAttempts) {
        git fetch origin $currentBranch
        $localCommit = git rev-parse HEAD
        $remoteCommit = git rev-parse "origin/$currentBranch"

        if ($localCommit -eq $remoteCommit) {
            $verified = $true
            break
        }

        $attempt++
        Write-Host "Waiting for commit to sync... (Attempt $attempt/$maxAttempts)"
        Start-Sleep -Seconds 2
    }

    if (-not $verified) {
        Write-Error "Failed to verify commit on remote after 3 minutes. Local and remote are out of sync."
        exit 1
    }

    Write-Host "Commit verified on remote. Creating and pushing tag..."
} else {
    Write-Host "Version files already match $version; nothing to commit. Creating and pushing tag..."
}

git tag $newTag
git push origin $newTag

Write-Host "Successfully created and pushed testing tag: $newTag"
