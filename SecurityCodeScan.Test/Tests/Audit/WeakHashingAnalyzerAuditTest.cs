﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SecurityCodeScan.Analyzers;
using SecurityCodeScan.Test.Config;
using SecurityCodeScan.Test.Helpers;

namespace SecurityCodeScan.Test.Audit
{
    [TestClass]
    public class WeakHashingAnalyzerAuditTest : DiagnosticVerifier
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers(string language)
        {
            return new DiagnosticAnalyzer[] { new WeakHashingAnalyzerCSharp(), new WeakHashingAnalyzerVisualBasic() };
        }

        [DataRow("CryptoConfig.CreateFromName(name)", true)]
        [DataRow("CryptoConfig.CreateFromName(name)", false)]
        [DataTestMethod]
        public async Task HashCreateAuditMode(string create, bool auditMode)
        {
            var cSharpTest = $@"
using System.Security.Cryptography;

class WeakHashing
{{
    static void Foo(string name)
    {{
        var sha = {create};
    }}
}}
";

            var visualBasicTest = $@"
Imports System.Security.Cryptography

Class WeakHashing
    Private Shared Sub Foo(name As System.String)
        Dim sha As HashAlgorithm = {create}
    End Sub
End Class
";

            var expected = new DiagnosticResult
            {
                Id       = "SCS0006",
                Severity = DiagnosticSeverity.Warning,
                Message  = "Possibly weak hashing function"
            };

            var testConfig = $@"
AuditMode: {auditMode}
";

            var optionsWithProjectConfig = ConfigurationTest.CreateAnalyzersOptionsWithConfig(testConfig);

            await VerifyCSharpDiagnostic(cSharpTest,
                                         auditMode ? new [] {expected} : null,
                                         auditMode ? optionsWithProjectConfig : null).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest,
                                              auditMode ? new[] { expected } : null,
                                              auditMode ? optionsWithProjectConfig : null).ConfigureAwait(false);
        }
    }
}
