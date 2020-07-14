﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.Docker.Tests
{
    [Trait("Category", "pre-build")]
    public class TemplateTests
    {
        private ITestOutputHelper OutputHelper { get; }

        public TemplateTests(ITestOutputHelper outputHelper)
        {
            OutputHelper = outputHelper;
        }

        [Fact]
        public void VerifyDockerfileTemplates()
        {
            string generateDockerfilesScript = Path.Combine(Config.SourceRepoRoot, "eng", "dockerfile-templates", "Get-GeneratedDockerfiles.ps1");
            string powershellArgs = $"-File {generateDockerfilesScript} -Validate";
            (Process Process, string StdOut, string StdErr) executeResult;

            // Support both execution within Windows 10, Nano Server and Linux environments.
            try
            {
                executeResult = ExecuteHelper.ExecuteProcess("pwsh", powershellArgs, OutputHelper);
            }
            catch (Win32Exception)
            {
                executeResult = ExecuteHelper.ExecuteProcess("powershell", powershellArgs, OutputHelper);
            }

            if (executeResult.Process.ExitCode != 0)
            {
                OutputHelper.WriteLine(
                    $"The Dockerfiles are out of sync with the templates.  Update the Dockerfiles by running `{generateDockerfilesScript}`.");
            }

            Assert.Equal(0, executeResult.Process.ExitCode);
        }
    }
}
