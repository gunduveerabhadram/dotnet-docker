// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Xunit.Abstractions;

namespace Microsoft.DotNet.Docker.Tests
{
    public class DockerHelper
    {
        public static string DockerOS => GetDockerOS();
        public static string ContainerWorkDir => IsLinuxContainerModeEnabled ? "/sandbox" : "c:\\sandbox";
        public static bool IsLinuxContainerModeEnabled => string.Equals(DockerOS, "linux", StringComparison.OrdinalIgnoreCase);
        private ITestOutputHelper OutputHelper { get; set; }

        public DockerHelper(ITestOutputHelper outputHelper)
        {
            OutputHelper = outputHelper;
        }

        public void Build(string dockerfile, string tag, string fromImage, params string[] buildArgs)
        {
            string buildArgsOption = $"--build-arg base_image={fromImage}";
            if (buildArgs != null)
            {
                foreach (string arg in buildArgs)
                {
                    buildArgsOption += $" --build-arg {arg}";
                }
            }

            ExecuteWithLogging($"build -t {tag} {buildArgsOption} -f {dockerfile} .");
        }

        public static bool ContainerExists(string name)
        {
            return ResourceExists("container", $"-f \"name={name}\"");
        }

        public void DeleteContainer(string container)
        {
            if (ContainerExists(container))
            {
                ExecuteWithLogging($"logs {container}", ignoreErrors: true);
                ExecuteWithLogging($"container rm -f {container}");
            }
        }

        public void DeleteImage(string tag)
        {
            if (ImageExists(tag))
            {
                ExecuteWithLogging($"image rm -f {tag}");
            }
        }

        public void DeleteVolume(string name)
        {
            if (VolumeExists(name))
            {
                ExecuteWithLogging($"volume rm -f {name}");
            }
        }

        private static string Execute(
            string args, bool ignoreErrors = false, bool autoRetry = false, ITestOutputHelper outputHelper = null)
        {
            (Process Process, string StdOut, string StdErr) result;
            if (autoRetry)
            {
                result = ExecuteWithRetry(args, outputHelper, ExecuteProcess);
            }
            else
            {
                result = ExecuteProcess(args, outputHelper);
            }

            if (!ignoreErrors && result.Process.ExitCode != 0)
            {
                ProcessStartInfo startInfo = result.Process.StartInfo;
                string msg = $"Failed to execute {startInfo.FileName} {startInfo.Arguments}{Environment.NewLine}{result.StdErr}";
                throw new InvalidOperationException(msg);
            }

            return result.StdOut;
        }

        private static (Process Process, string StdOut, string StdErr) ExecuteProcess(
            string args, ITestOutputHelper outputHelper)
        {
            Process process = new Process
            {
                EnableRaisingEvents = true,
                StartInfo =
                {
                    FileName = "docker",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            StringBuilder stdOutput = new StringBuilder();
            process.OutputDataReceived += new DataReceivedEventHandler((sender, e) => stdOutput.AppendLine(e.Data));

            StringBuilder stdError = new StringBuilder();
            process.ErrorDataReceived += new DataReceivedEventHandler((sender, e) => stdError.AppendLine(e.Data));

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            string output = stdOutput.ToString().Trim();
            if (outputHelper != null && !string.IsNullOrWhiteSpace(output))
            {
                outputHelper.WriteLine(output);
            }

            string error = stdError.ToString().Trim();
            if (outputHelper != null && !string.IsNullOrWhiteSpace(error))
            {
                outputHelper.WriteLine(error);
            }

            return (process, output, error);
        }

        private string ExecuteWithLogging(string args, bool ignoreErrors = false, bool autoRetry = false)
        {
            OutputHelper.WriteLine($"Executing : docker {args}");
            return Execute(args, outputHelper: OutputHelper, ignoreErrors: ignoreErrors, autoRetry: autoRetry);
        }

        private static (Process Process, string StdOut, string StdErr) ExecuteWithRetry(
            string args,
            ITestOutputHelper outputHelper,
            Func<string, ITestOutputHelper, (Process Process, string StdOut, string StdErr)> executor)
        {
            const int maxRetries = 5;
            const int waitFactor = 5;

            int retryCount = 0;

            (Process Process, string StdOut, string StdErr) result = executor(args, outputHelper);
            while (result.Process.ExitCode != 0)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    break;
                }

                int waitTime = Convert.ToInt32(Math.Pow(waitFactor, retryCount - 1));
                if (outputHelper != null)
                {
                    outputHelper.WriteLine($"Retry {retryCount}/{maxRetries}, retrying in {waitTime} seconds...");
                }

                Thread.Sleep(waitTime * 1000);
                result = executor(args, outputHelper);
            }

            return result;
        }

        private static string GetDockerOS()
        {
            return Execute("version -f \"{{ .Server.Os }}\"");
        }

        public string GetContainerAddress(string container)
        {
            return ExecuteWithLogging("inspect -f \"{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}\" " + container);
        }

        public string GetContainerHostPort(string container, int containerPort = 80)
        {
            return ExecuteWithLogging(
                $"inspect -f \"{{{{(index (index .NetworkSettings.Ports \\\"{containerPort}/tcp\\\") 0).HostPort}}}}\" {container}");
        }

        public string GetContainerWorkPath(string relativePath)
        {
            string separator = IsLinuxContainerModeEnabled ? "/" : "\\";
            return $"{ContainerWorkDir}{separator}{relativePath}";
        }

        public static bool ImageExists(string tag)
        {
            return ResourceExists("image", tag);
        }

        public void Pull(string image)
        {
            ExecuteWithLogging($"pull {image}", autoRetry: true);
        }

        private static bool ResourceExists(string type, string filterArg)
        {
            string output = Execute($"{type} ls -q {filterArg}", true);
            return output != "";
        }

        public void Run(
            string image,
            string command,
            string containerName,
            string volumeName = null,
            string portPublishArgs = "-p 80",
            bool detach = false,
            bool runAsContainerAdministrator = false)
        {
            string volumeArg = volumeName == null ? string.Empty : $" -v {volumeName}:{ContainerWorkDir}";
            string userArg = runAsContainerAdministrator ? " -u ContainerAdministrator" : string.Empty;
            string detachArg = detach ? " -d  -t " : string.Empty;
            ExecuteWithLogging($"run --rm --name {containerName}{volumeArg}{userArg}{detachArg} {portPublishArgs} {image} {command}");
        }

        public static bool VolumeExists(string name)
        {
            return ResourceExists("volume", $"-f \"name={name}\"");
        }
    }
}
