[CmdletBinding()]
Param
(
	[parameter(Position=0, Mandatory=$true)]
	[ValidateNotNullOrEmpty()]
    [string]$InitializeScript,
	[parameter(Position=1, Mandatory=$true)]
	[ValidateNotNullOrEmpty()]
	[string]$Url
)

function Download([string]$url, [string]$file)
{
    [bool]$output = $false
    $fileExist = Test-Path $file
    if ($fileExist)
    {
        Write-Warning  "$file already exist!"
        $output = $true
        return $output
    }

	Write-Verbose "Starting to download file $url as $file"
    try
    {
	    #Invoke-WebRequest -Uri $url -OutFile $file
	    $webclient = New-Object System.Net.WebClient
        $webclient.DownloadFile($url, $file)
        Write-Host "Download completed."
        $output = $true
    }
    catch
    {
        Write-Error "Caught an exception:"
        Write-Error "Exception Type: $($_.Exception.GetType().FullName)"
        Write-Error "Exception Message: $($_.Exception.Message)"
    }

    return $output
}

function Extract([string]$file, [string]$path)
{
    [bool]$output = $false
    $fileExist = Test-Path $file
    if (-not $fileExist)
    {
        Write-Warning "$file does not exist!"
        return $output
    }
    $pathExist = Test-Path $path
	if ($pathExist)
	{
		Write-Warning "$path already exist, no extraction will be done"
        $output = $true
	} else {
		Write-Verbose "Starting to extract from $file to $path"
		Add-Type -assembly "System.IO.Compression.FileSystem"
		[System.IO.Compression.ZipFile]::ExtractToDirectory($file, $path)
		Write-Host "Extract completed."
        $output = $true
	}
    return $output
}

function Initialize([string] $workingDirectory, [string] $scriptsDirectory, [string]$scriptsUrl, [string]$InitializeScript)
{
	[string] $scriptsPath = [System.IO.Path]::Combine($workingDirectory, $scriptsDirectory)
	[string] $scriptsFile = [System.IO.Path]::Combine($workingDirectory, $scriptsDirectory + [System.IO.Path]::GetExtension($scriptsUrl))
	Write-Verbose "SCRIPTS_PATH: $scriptsPath"

    $fileExist = Test-Path $scriptsPath
    if ($fileExist)
    {
        Write-Warning  "$scriptsPath already exist!"
    }
    else
    {
	    $downloadSuccess = Download $scriptsUrl $scriptsFile
	    if ($downloadSuccess = $true)
	    {
		    $extractSuccess = Extract $scriptsFile $scriptsPath
		    if ($extractSuccess = $true)
		    {
			    $moveFileExist = Test-Path $scriptsFile
			    if ($moveFileExist)
			    {
				    Write-Verbose "Starting to move file $scriptsFile to $scriptsPath"
				    Move-Item $scriptsFile $scriptsPath -force
				    Write-Host "Move completed."
			    }
		    }
	    }
    }

    $scriptsPath = [System.IO.Path]::Combine($scriptsPath, $InitializeScript)
    Write-Verbose "Calling $scriptsPath"

    & $scriptsPath -Build -ApplyVersion
	Write-Verbose "The End!"
}

Clear-Host
$VerbosePreference = "Continue"

Write-Verbose "RUNNING_SCRIPT: $($MyInvocation.MyCommand.Path)"
Write-Verbose "CURRENT_DIRECTORY: $([System.Environment]::CurrentDirectory)"

[string] $runningDirectory = (Get-Item -Path ".\" -Verbose).FullName
Write-Verbose "RUNNING_DIRECTORY: $runningDirectory"

(Initialize $runningDirectory "OneScriptWay" $Url $InitializeScript)