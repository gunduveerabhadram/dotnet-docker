// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.VersionTools.Dependencies;

#nullable enable
namespace Dotnet.Docker;

internal class ChiselToolUpdater : VariableUpdaterBase
{
    private readonly string _dockerfileVersion;
    private readonly string _newValue;

    public ChiselToolUpdater(string repoRoot, string variableName, string dockerfileVersion, string newValue) : base(repoRoot, variableName)
    {
        _dockerfileVersion = dockerfileVersion;
        _newValue = newValue;
    }

    protected sealed override string TryGetDesiredValue(IEnumerable<IDependencyInfo> dependencyInfos, out IEnumerable<IDependencyInfo> usedDependencyInfos)
    {
        IDependencyInfo? runtimeDependencyInfo = dependencyInfos.FirstOrDefault(info => info.SimpleName == "runtime");
        usedDependencyInfos = Enumerable.Empty<IDependencyInfo>();

        string currentChsielToolVersion = ManifestHelper.GetVariableValue(VariableName, ManifestVariables.Value);

        // Avoid updating the chisel tooling if we are updating a runtime
        // version that doesn't ship chiseled images
        if (runtimeDependencyInfo is null || !VariableName.Contains(_dockerfileVersion))
        {
            return currentChsielToolVersion;
        }

        // Avoid updating chisel tooling unless we already know we are
        // rebuilding at least the runtime images, since changing the chisel
        // tool shouldn't make a difference in the output image.
        string runtimeVariableName = ManifestHelper.GetVersionVariableName(VersionType.Build, "runtime", _dockerfileVersion);
        string currentRuntimeVersion = ManifestHelper.GetVariableValue(runtimeVariableName, ManifestVariables.Value);
        if (runtimeDependencyInfo.SimpleVersion == currentRuntimeVersion)
        {
            return currentChsielToolVersion;
        }

        usedDependencyInfos = new[] { runtimeDependencyInfo };
        return _newValue;
    }
}
