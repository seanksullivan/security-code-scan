﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SecurityCodeScan.Analyzers;
using SecurityCodeScan.Test.Helpers;
using System.Web.Script.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SecurityCodeScan.Analyzers.Taint;
using SecurityCodeScan.Test.Config;

namespace SecurityCodeScan.Test
{
    [TestClass]
    public class UnsafeDeserializationAnalyzerTests : DiagnosticVerifier
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers(string language)
        {
            return new DiagnosticAnalyzer[]
            {
                new UnsafeDeserializationAnalyzerCSharp(),
                new UnsafeDeserializationAnalyzerVisualBasic(),
                new TaintAnalyzerCSharp(),
                new TaintAnalyzerVisualBasic()
            };
        }

        private static readonly PortableExecutableReference[] References =
        {
            MetadataReference.CreateFromFile(typeof(JavaScriptSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(JsonSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Web.Mvc.Controller).Assembly.Location)
        };

        protected override IEnumerable<MetadataReference> GetAdditionalReferences() => References;

        [TestCategory("Detect")]
        [TestMethod]
        public async Task DetectJavaScriptSerializerWithSimpleTypeResolverUsed()
        {
            var cSharpTest = @"
using System.Web.Script.Serialization;

namespace VulnerableApp
{
    class Test
    {
        private JavaScriptSerializer serializer = new JavaScriptSerializer(new SimpleTypeResolver());
    }
}
";

            var visualBasicTest = @"
Imports System.Web.Script.Serialization

Namespace VulnerableApp
    Class Test
        Private Dim serializer = new JavaScriptSerializer(new SimpleTypeResolver())
    End Class
End Namespace
";

            var expected = new DiagnosticResult()
            {
                Id       = "SCS0028",
                Severity = DiagnosticSeverity.Warning
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(8, 51)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(6, 34)).ConfigureAwait(false);
        }

        [TestCategory("Detect")]
        [TestMethod]
        public async Task DetectJavaScriptSerializerWithSimpleTypeResolverAsFieldUsed()
        {
            var cSharpTest = @"
using System.Web.Script.Serialization;

namespace VulnerableApp
{
    class Test
    {
        private static SimpleTypeResolver resolver = new SimpleTypeResolver();
        private static JavaScriptSerializer serializer = new JavaScriptSerializer(resolver);
    }
}
";

            var visualBasicTest = @"
Imports System.Web.Script.Serialization

Namespace VulnerableApp
    Class Test
        Private Shared Dim resolver = new SimpleTypeResolver()
        Private Shared Dim serializer = new JavaScriptSerializer(resolver)
    End Class
End Namespace
";

            var expected = new DiagnosticResult()
            {
                Id       = "SCS0028",
                Severity = DiagnosticSeverity.Warning
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(9, 58)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(7, 41)).ConfigureAwait(false);
        }

        [TestCategory("Safe")]
        [TestMethod]
        public async Task IgnoreJavaScriptSerializerWithNotCompilingParameterUsed()
        {
            var cSharpTest = @"
using System.Web.Script.Serialization;

namespace VulnerableApp
{
    class Test
    {
        private static JavaScriptSerializer serializer = new JavaScriptSerializer(resolver);
    }
}
";

            var visualBasicTest = @"
Imports System.Web.Script.Serialization

Namespace VulnerableApp
    Class Test
        Private Shared Dim serializer = new JavaScriptSerializer(resolver)
    End Class
End Namespace
";

            await VerifyCSharpDiagnostic(cSharpTest, new DiagnosticResult { Id = "CS0103" }).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, new DiagnosticResult { Id = "BC30451" }).ConfigureAwait(false);
        }

        [TestCategory("Safe")]
        [TestMethod]
        public async Task IgnoreJavaScriptSerializerUsed()
        {
            var cSharpTest = @"
using System.Web.Script.Serialization;

namespace VulnerableApp
{
    class Test
    {
        private JavaScriptSerializer serializer = new JavaScriptSerializer();
    }
}
";

            var visualBasicTest = @"
Imports System.Web.Script.Serialization

Namespace VulnerableApp
    Class Test
        Private Dim serializer = new JavaScriptSerializer()
    End Class
End Namespace
";

            await VerifyCSharpDiagnostic(cSharpTest).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest).ConfigureAwait(false);
        }

        [TestCategory("Detect")]
        [DataTestMethod]
        [DataRow("Objects")]
        [DataRow("Arrays")]
        [DataRow("All")]
        [DataRow("Auto")]
        public async Task DetectJSonSerializerTypeNameHandlingNotNoneOnProperty(string property)
        {
            var cSharpTest = $@"
using Newtonsoft.Json;

namespace VulnerableApp
{{
    class Test
    {{
        [JsonProperty(TypeNameHandling = TypeNameHandling.{property})]
        private string Property {{ get; set; }}
    }}
}}
";

            var visualBasicTest = $@"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        <JsonProperty(TypeNameHandling := TypeNameHandling.{property})>
        Public Property TestProperty As String
            Get
                Return ""Test""
            End Get
            Set(value As String)
            End Set
        End Property
    End Class
End Namespace
";

            var expected = new DiagnosticResult()
            {
                Id       = "SCS0028",
                Severity = DiagnosticSeverity.Warning
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(8, 42)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(6, 43)).ConfigureAwait(false);
        }

        [TestCategory("Safe")]
        [TestMethod]
        public async Task IgnoreJSonSerializerTypeNameHandlingNoneOnProperty()
        {
            var cSharpTest = @"
using Newtonsoft.Json;

namespace VulnerableApp
{
    class Test
    {
        [JsonProperty(TypeNameHandling = TypeNameHandling.None)]
        private string Property { get; set; }
    }
}
";

            var visualBasicTest = @"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        <JsonProperty(TypeNameHandling := TypeNameHandling.None)>
        Public Property TestProperty As String
            Get
                Return ""Test""
            End Get
            Set(value As String)
            End Set
        End Property
    End Class
End Namespace
";

            await VerifyCSharpDiagnostic(cSharpTest).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest).ConfigureAwait(false);
        }

        [TestCategory("Safe")]
        [TestMethod]
        [Ignore("ignore the TypeNameHandling on deep clone (although it doesn't clone private members)")]
        public async Task IgnoreJSonSerializerTypeNameHandlingDeepclone()
        {
            var cSharpTest = @"
using Newtonsoft.Json;

namespace VulnerableApp
{
    class Test
    {
        public T DeepClone<T>(T source)
        {
            var serializeSettings = new JsonSerializerSettings {TypeNameHandling = TypeNameHandling.All};
            var serialized = JsonConvert.SerializeObject(source, serializeSettings);
            return JsonConvert.DeserializeObject<T>(serialized, serializeSettings);
        }
    }
}
";

            await VerifyCSharpDiagnostic(cSharpTest).ConfigureAwait(false);
        }

        [TestCategory("Detect")]
        [DataTestMethod]
        [DataRow("Objects")]
        [DataRow("Arrays")]
        [DataRow("All")]
        [DataRow("Auto")]
        public async Task DetectJSonSerializerTypeNameHandlingNotNone(string property)
        {
            var cSharpTest = $@"
using Newtonsoft.Json;

namespace VulnerableApp
{{
    class Test
    {{
        static void TestDeserialization()
        {{
             var settings = new JsonSerializerSettings
                {{
                    TypeNameHandling = TypeNameHandling.{property}
                }};
        }}
    }}
}}
";

            var visualBasicTest = $@"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        Private Sub TestDeserialization()
            Dim settings = New JsonSerializerSettings With _
                {{
                    .TypeNameHandling = TypeNameHandling.{property}
                }}
        End Sub
    End Class
End Namespace
";

            var expected = new DiagnosticResult()
            {
                Id       = "SCS0028",
                Severity = DiagnosticSeverity.Warning
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(12, 40)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(9, 41)).ConfigureAwait(false);
        }

        [TestCategory("Detect")]
        [TestMethod]
        public async Task DetectJSonSerializerTypeNameHandlingAllWithNamespace()
        {
            var cSharpTest = @"
namespace VulnerableApp
{
    class Test
    {
        static void TestDeserialization()
        {
             var settings = new Newtonsoft.Json.JsonSerializerSettings
                {
                    TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All
                };
        }
    }
}
";

            var visualBasicTest = @"
Namespace VulnerableApp
    Class Test
        Private Sub TestDeserialization()
            Dim settings = New Newtonsoft.Json.JsonSerializerSettings With _
                {
                    .TypeNameHandling = Newtonsoft.Json.TypeNameHandling.All
                }
        End Sub
    End Class
End Namespace
";

            var expected = new DiagnosticResult()
            {
                Id       = "SCS0028",
                Severity = DiagnosticSeverity.Warning,
                Message  = "TypeNameHandling is set to other value than 'None' that may lead to deserialization vulnerability"
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(10, 40)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(7, 41)).ConfigureAwait(false);
        }

        [TestCategory("Detect")]
        [TestMethod]
        public async Task DetectJSonSerializerTypeNameHandlingAllFromConstant()
        {
            var cSharpTest = @"
using Newtonsoft.Json;

namespace VulnerableApp
{
    class Test
    {
        static void TestDeserialization()
        {
             var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = (TypeNameHandling)3
                };
        }
    }
}
";

            var visualBasicTest = @"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        Private Sub TestDeserialization()
            Dim settings = New JsonSerializerSettings With
                {
                    .TypeNameHandling = 3
                }
        End Sub
    End Class
End Namespace
";

            var expected = new DiagnosticResult()
            {
                Id       = "SCS0028",
                Severity = DiagnosticSeverity.Warning
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(12, 40)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(9, 41)).ConfigureAwait(false);
        }

        [TestCategory("Detect")]
        [TestMethod]
        public async Task DetectJSonSerializerTypeNameHandlingAllAfterSettingsConstruction()
        {
            var cSharpTest = @"
using Newtonsoft.Json;

namespace VulnerableApp
{
    class Test
    {
        static void TestDeserialization()
        {
             var settings = new JsonSerializerSettings();
             settings.TypeNameHandling = TypeNameHandling.All;
        }
    }
}
";

            var visualBasicTest = @"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        Private Sub TestDeserialization()
            Dim settings = New JsonSerializerSettings()
             settings.TypeNameHandling = TypeNameHandling.All
        End Sub
    End Class
End Namespace
";

            var expected = new DiagnosticResult()
            {
                Id       = "SCS0028",
                Severity = DiagnosticSeverity.Warning
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(11, 42)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(8, 42)).ConfigureAwait(false);
        }

        [TestCategory("Detect")]
        [TestMethod]
        public async Task DetectJsonSerializerTypeNameHandlingUnknownRuntimeValue()
        {
            var cSharpTest = @"
using Newtonsoft.Json;
using System.Web.Mvc;

namespace VulnerableApp
{
    class Test : Controller
    {
        public void TestDeserialization(TypeNameHandling param)
        {
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = param
                };
        }
    }
}
";

            var visualBasicTest = @"
Imports Newtonsoft.Json
Imports System.Web.Mvc

Namespace VulnerableApp
    Class Test
        Inherits Controller

        Public Sub TestDeserialization(param As TypeNameHandling)
            Dim settings = New JsonSerializerSettings With _
                {
                    .TypeNameHandling = param
                }
        End Sub
    End Class
End Namespace
";
            var expected = new DiagnosticResult
            {
                Id       = "SCS0028",
                Severity = DiagnosticSeverity.Warning,
                Message  = "Possibly unsafe deserialization setting enabled"
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(13, 21)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(12, 21)).ConfigureAwait(false);
        }

        [TestCategory("Safe")]
        [TestMethod]
        public async Task IgnoreJSonSerializerTypeNameHandlingNone()
        {
            var cSharpTest = @"
using Newtonsoft.Json;

namespace VulnerableApp
{
    class Test
    {
        static void TestDeserialization()
        {
             var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None
                };
        }
    }
}
";

            var visualBasicTest = @"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        Private Sub TestDeserialization()
            Dim settings = New JsonSerializerSettings With _
                {
                    .TypeNameHandling = TypeNameHandling.None
                }
        End Sub
    End Class
End Namespace
";

            await VerifyCSharpDiagnostic(cSharpTest).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest).ConfigureAwait(false);
        }

        [DataTestMethod]
        [DataRow("TypeNameHandling.Test", new[] { "CS0117" },            new[] { "BC30456" },            false)]
        [DataRow("foo()",                 new[] { "CS0029" },            new[] { "BC30311" },            false)]
        [DataRow("foo2(xyz)",             new[] { "SCS0028", "CS0103" }, new[] { "SCS0028", "BC30451" }, true)]
        public async Task JSonSerializerTypeNameHandlingNonCompilingValue(string right, string[] csErrors, string[] vbErrors, bool audit)
        {
            var cSharpTest = $@"
using Newtonsoft.Json;

namespace VulnerableApp
{{
    class Test
    {{
        static void TestDeserialization()
        {{
             var settings = new JsonSerializerSettings
                {{
                    TypeNameHandling = {right}
                }};
        }}

        static Test foo()
        {{
            return null;
        }}

        static TypeNameHandling foo2(string a)
        {{
            return TypeNameHandling.All;
        }}
    }}
}}
";

            var visualBasicTest = $@"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        Private Sub TestDeserialization()
            Dim settings = New JsonSerializerSettings With _
                {{
                    .TypeNameHandling = {right}
                }}
        End Sub

        Private Function foo() As Test
            Return Nothing
        End Function

        Private Function foo2(a As String) As TypeNameHandling
            Return TypeNameHandling.All
        End Function
    End Class
End Namespace
";

            var testConfig = @"
AuditMode: true
";
            var optionsWithProjectConfig = ConfigurationTest.CreateAnalyzersOptionsWithConfig(testConfig);
            await VerifyCSharpDiagnostic(cSharpTest,
                                         csErrors.Select(x => new DiagnosticResult { Id = x }.WithLocation(12)).ToArray(),
                                         audit ? optionsWithProjectConfig : null)
                .ConfigureAwait(false);

            await VerifyVisualBasicDiagnostic(visualBasicTest,
                                              vbErrors.Select(x => new DiagnosticResult { Id = x }.WithLocation(9)).ToArray(),
                                              audit ? optionsWithProjectConfig : null)
                .ConfigureAwait(false);
        }

        [TestCategory("Safe")]
        [TestMethod]
        public async Task IgnoreJSonSerializerTypeNameHandlingNonCompilingStringValue()
        {
            var cSharpTest = @"
using Newtonsoft.Json;

namespace VulnerableApp
{
    class Test
    {
        static void TestDeserialization()
        {
             var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = ""test""
                };
        }
    }
}
";

            var visualBasicTest = @"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        Private Sub TestDeserialization()
            Dim settings = New JsonSerializerSettings With _
                {
                    .TypeNameHandling = ""test""
                }
        End Sub
    End Class
End Namespace
";

            await VerifyCSharpDiagnostic(cSharpTest, new DiagnosticResult { Id = "CS0029" }.WithLocation(12)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest).ConfigureAwait(false);
        }

        [TestCategory("Safe")]
        [TestMethod]
        public async Task IgnoreJsonSerializerTypeNameHandlingNonCompilingTypeAssigned()
        {
            var cSharpTest = @"
using Newtonsoft.Json;

namespace VulnerableApp
{
    class Test
    {
        static void TestDeserialization()
        {
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = new System.Exception()
                };
        }
    }
}
";

            var visualBasicTest = @"
Imports Newtonsoft.Json

Namespace VulnerableApp
    Class Test
        Private Sub TestDeserialization()
            Dim settings = New JsonSerializerSettings With _
                {
                    .TypeNameHandling = new System.Exception()
                }
        End Sub
    End Class
End Namespace
";

            await VerifyCSharpDiagnostic(cSharpTest, new DiagnosticResult { Id = "CS0029" }.WithLocation(12)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, new DiagnosticResult { Id = "BC30311" }.WithLocation(9)).ConfigureAwait(false);
        }

        [TestCategory("Detect")]
        [TestMethod]
        public async Task GivenAliasDirective_DetectDiagnostic()
        {
            var cSharpTest = @"
using System.Web.Script.Serialization;
using JSS = System.Web.Script.Serialization.JavaScriptSerializer;

namespace VulnerableApp
{
    class Test
    {
        private JSS serializer = new JSS(new SimpleTypeResolver());
    }
}
";
            var visualBasicTest = @"
Imports System.Web.Script.Serialization
Imports JSS = System.Web.Script.Serialization.JavaScriptSerializer

Namespace VulnerableApp
    Class Test
        Private Dim serializer = new JSS(new SimpleTypeResolver())
    End Class
End Namespace
";
            var expected = new DiagnosticResult()
            {
                Id = "SCS0028",
                Severity = DiagnosticSeverity.Warning
            };

            await VerifyCSharpDiagnostic(cSharpTest, expected.WithLocation(9, 34)).ConfigureAwait(false);
            await VerifyVisualBasicDiagnostic(visualBasicTest, expected.WithLocation(7, 34)).ConfigureAwait(false);
        }
    }
}
