﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using SecurityCodeScan.Test.Config;

namespace SecurityCodeScan.Test.Audit
{
    public class AuditTest : ConfigurationTest
    {
        private const string ConfigPath = @"Config\AuditMode.yml";

        // Multi-thread safe initialization, guaranteed to be called only once
        private static readonly Lazy<Task<AnalyzerOptions>> Config = new Lazy<Task<AnalyzerOptions>>(async () =>
                                                                                {
                                                                                    using (var file = File.OpenText(ConfigPath))
                                                                                    {
                                                                                        var testConfig = await file.ReadToEndAsync().ConfigureAwait(false);
                                                                                        return CreateAnalyzersOptionsWithConfig(testConfig);
                                                                                    }
                                                                                });

        public static async Task<AnalyzerOptions> GetAuditModeConfigOptions()
        {
            return await Config.Value.ConfigureAwait(false);
        }
    }
}
