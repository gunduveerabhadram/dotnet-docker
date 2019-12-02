﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.DotNet.Docker.Tests
{
    public class RuntimeImageTests : CommonRuntimeImageTests
    {
        public RuntimeImageTests(ITestOutputHelper outputHelper)
            : base(outputHelper)
        {
        }

        protected override DotNetImageType ImageType => DotNetImageType.Runtime;

        [Theory]
        [MemberData(nameof(GetImageData))]
        public async Task VerifyAppScenario(ImageData imageData)
        {
            ImageScenarioVerifier verifier = new ImageScenarioVerifier(imageData, DockerHelper, OutputHelper);
            await verifier.Execute();
        }

        [Theory]
        [MemberData(nameof(GetImageData))]
        public void VerifyRuntimeDepsEnvironmentVariables(ImageData imageData)
        {
            base.VerifyEnvironmentVariables(imageData);
        }
    }
}
