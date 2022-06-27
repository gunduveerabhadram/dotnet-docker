#!/usr/bin/env pwsh
#
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

[cmdletbinding()]
param(
    [string]$Version,
    [string]$Architecture,
    [string]$OS,
    [string]$Registry,
    [string]$RepoPrefix,
    [switch]$DisableHttpVerification,
    [switch]$PullImages,
    [string]$ImageInfoPath,
    [ValidateSet("runtime", "runtime-deps", "aspnet", "sdk", "pre-build", "sample", "image-size", "monitor")]
    [string[]]$TestCategories = @("runtime", "runtime-deps", "aspnet", "sdk", "monitor"),
    [securestring]$SasQueryString,
    [securestring]$NuGetFeedPassword
)

function Log {
    param ([string] $Message)

    Write-Output $Message
}

function Exec {
    param ([string] $Cmd)

    Log "Executing: '$Cmd'"
    Invoke-Expression $Cmd
    if ($LASTEXITCODE -ne 0) {
        throw "Failed: '$Cmd'"
    }
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$EngCommonDir = "$PSScriptRoot/../eng/common"

$DotnetInstallDir = "$PSScriptRoot/../.dotnet"
& $EngCommonDir/Install-DotNetSdk.ps1 -InstallPath $DotnetInstallDir

# Ensure that ImageBuilder image is pulled because some tests require it
& $EngCommonDir/Get-ImageBuilder.ps1

$activeOS = docker version -f "{{ .Server.Os }}"

Push-Location "$PSScriptRoot\Microsoft.DotNet.Docker.Tests"

# Store the original set of environment variables before we start modifying them
$origEnvVars = Get-ChildItem env:

Try {
    # Run Tests
    if ([string]::IsNullOrWhiteSpace($Architecture)) {
        $Architecture = "amd64"
    }

    if ($DisableHttpVerification) {
        $env:DISABLE_HTTP_VERIFICATION = 1
    }
    else {
        $env:DISABLE_HTTP_VERIFICATION = $null
    }

    if ($PullImages) {
        $env:PULL_IMAGES = 1
    }
    else {
        $env:PULL_IMAGES = $null
    }

    # By default, account for the OS having a -slim variant, except for mariner which has a -distroless variant that needs to be tested separately.
    if (-not $OS.Contains("mariner")) {
        $OS += "*"
    }

    # PR builds group image build and test by runtime version.
    # CI builds group image build by the same criteria but run tests in separate jobs that are grouped by image version.
    # The distinction is not apparent for images such as 'runtime', 'aspnet', and 'sdk' because
    # their major.minor versions for the image and the product are the same.
    # However, other images such as 'monitor' can have a product version that is distinct from the runtime version.
    # Thus, filter images by runtime version in PR builds and by image version in non-PR builds.
    if (($env:BUILD_REASON -eq 'PullRequest') -or ($env:SYSTEM_PULLREQUEST_PULLREQUESTID)) {
        $env:RUNTIME_VERSION = $Version
    } else {
        $env:IMAGE_VERSION = $Version
    }

    $env:IMAGE_ARCH = $Architecture
    $env:IMAGE_OS = $OS
    $env:REGISTRY = $Registry
    $env:REPO_PREFIX = $RepoPrefix
    $env:IMAGE_INFO_PATH = $ImageInfoPath
    $env:SOURCE_REPO_ROOT = (Get-Item "$PSScriptRoot").Parent.FullName
    $env:SOURCE_BRANCH = & $PSScriptRoot/../eng/Get-Branch.ps1

    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'

    if ($SasQueryString) {
        $env:SAS_QUERY_STRING = ConvertFrom-SecureString $SasQueryString -AsPlainText
    }
    
    if ($NuGetFeedPassword) {
        $env:NUGET_FEED_PASSWORD = ConvertFrom-SecureString $NuGetFeedPassword -AsPlainText
    }

    $testFilter = ""
    if ($TestCategories) {
        # Construct an expression that filters the test to each of the
        # selected TestCategories (using an OR operator between each category).
        # See https://docs.microsoft.com/en-us/dotnet/core/testing/selective-unit-tests
        $TestCategories | foreach {
            # Skip pre-build tests on Windows because of missing pre-reqs (https://github.com/dotnet/dotnet-docker/issues/2261)
            if ($_ -eq "pre-build" -and $activeOS -eq "windows") {
                Write-Warning "Skipping pre-build tests for Windows containers"
            }
            else {
                if ($testFilter) {
                    $testFilter += "|"
                }

                $testFilter += "Category=$_"
            }
        }

        if (-not $testFilter) {
            exit;
        }

        $testFilter = "--filter `"$testFilter`""
    }

    Exec "$DotnetInstallDir/dotnet test $testFilter --logger:trx"

    if ($TestCategories.Contains('image-size')) {
        & ../performance/Validate-ImageSize.ps1 -PullImages:$PullImages -ValidationMode Integrity
    }
}
Finally {
    Pop-Location

    # Delete any newly added environment variables
    Get-ChildItem env: | Where-Object { $_.Name -notin ($origEnvVars | Select-Object -ExpandProperty Name) } | Remove-Item

    # Restore the original values of any modified environment variables
    $origEnvVars | ForEach-Object { Set-Item "env:$($_.Name)" $_.Value }
}
