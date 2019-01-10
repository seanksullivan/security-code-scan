﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SecurityCodeScan.Analyzers;
using SecurityCodeScan.Analyzers.Taint;

namespace SecurityCodeScan.Test.XXE
{
    [TestClass]
    public class XxeAnalyzerTaintTest : XxeAnalyzerTestBase
    {
        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers(string language)
        {
            return new DiagnosticAnalyzer[] { new XxeDiagnosticAnalyzerCSharp(), new XxeDiagnosticAnalyzerVisualBasic(), new TaintAnalyzerCSharp(), new TaintAnalyzerVisualBasic(), };
        }

        [TestCategory("Safe")]
        [TestMethod]
        [Ignore("taint transfer is not implemented")]
        public async Task XmlDocumentLoadTaint()
        {
            const string cSharpTest = @"
using System.Xml;

class Xxe
{
    public static void parseUpload(string path)
    {
        var xmlDoc = new XmlDocument();
        var xmlDoc2 = xmlDoc;
        xmlDoc2.XmlResolver = null;
        xmlDoc.Load(path);
    }
}";

            const string visualBasicTest = @"
Imports System.Xml

Class Xxe
    Public Shared Sub parseUpload(path As String)
        Dim xmlDoc As New XmlDocument()
        Dim xmlDoc2 = xmlDoc;
        xmlDoc2.XmlResolver = Nothing;
        xmlDoc.Load(path)
    End Sub
End Class
";

            await VerifyNoWarnings(cSharpTest, visualBasicTest).ConfigureAwait(false);
        }

        // although the input is untrusted
        // there is not xxe or path injection
        [TestCategory("Safe")]
        [TestMethod]
        public async Task StringReader()
        {
            const string cSharpTest = @"
using System.IO;
using System.Xml;

class PathTraversal
{
    public static void Run(string strText)
    {
        using (var reader = XmlReader.Create(new StringReader(strText)))
        {
            reader.Read();
        }
    }
}
";

            const string visualBasicTest = @"
Imports System.IO
Imports System.Xml

Class PathTraversal
    Public Shared Sub Run(strText As String)
        Using reader = XmlReader.Create(New StringReader(strText))
            reader.Read()
        End Using
    End Sub
End Class
";

            await VerifyNoWarnings(cSharpTest, visualBasicTest).ConfigureAwait(false);
        }

        // todo: introduce configuration setting to show questionable findings
        // add tests where ProhibitDtd, DtdProcessing or entire XmlReaderSettings comes from untrusted source
        //[DataRow("XmlReader.Create(default(Stream))")]
        //[DataRow("XmlReader.Create(default(Stream), xmlReaderSettingsInput)")]
        //[DataRow("XmlReader.Create(default(Stream), xmlReaderSettingsInput, default(string))")]
        //[DataRow("XmlReader.Create(default(Stream), xmlReaderSettingsInput, default(XmlParserContext))")]
        //[DataRow("XmlReader.Create(default(TextReader))")]
        //[DataRow("XmlReader.Create(default(TextReader), xmlReaderSettingsInput)")]
        //[DataRow("XmlReader.Create(default(TextReader), xmlReaderSettingsInput, default(string))")]
        //[DataRow("XmlReader.Create(default(TextReader), xmlReaderSettingsInput, default(XmlParserContext))")]
        //[DataRow("XmlReader.Create(default(TextReader), xmlReaderSettingsInput)")]
    }
}
