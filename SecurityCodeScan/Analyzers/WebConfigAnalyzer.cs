﻿using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SecurityCodeScan.Analyzers.Locale;
using SecurityCodeScan.Analyzers.Utils;

namespace SecurityCodeScan.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    public class WebConfigAnalyzer : DiagnosticAnalyzer, IExternalFileAnalyzer
    {
        public static readonly DiagnosticDescriptor RuleValidateRequest         = LocaleUtil.GetDescriptor("SCS0021");
        public static readonly DiagnosticDescriptor RuleRequestValidationMode   = LocaleUtil.GetDescriptor("SCS0030");
        public static readonly DiagnosticDescriptor RuleEnableEventValidation   = LocaleUtil.GetDescriptor("SCS0022");
        public static readonly DiagnosticDescriptor RuleViewStateEncryptionMode = LocaleUtil.GetDescriptor("SCS0023");
        public static readonly DiagnosticDescriptor RuleEnableViewStateMac      = LocaleUtil.GetDescriptor("SCS0024");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(RuleValidateRequest,
                                                                                                           RuleRequestValidationMode,
                                                                                                           RuleEnableEventValidation,
                                                                                                           RuleViewStateEncryptionMode,
                                                                                                           RuleEnableViewStateMac);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCompilationAction(Compilation);
        }

        private void Compilation(CompilationAnalysisContext ctx)
        {
            //Load Web.config files : ASP.net web application configuration
            foreach (AdditionalText file in ctx.Options
                                               .AdditionalFiles
                                               .Where(file => Path.GetFileName(file.Path).StartsWith("Web.config")))
            {
                AnalyzeFile(file, ctx);
            }
        }

        private string CheckAttribute(XElement                   element,
                                      string                     attributeName,
                                      string                     defaultValue,
                                      Func<string, bool>         isGoodValue,
                                      DiagnosticDescriptor       diagnosticDescriptor,
                                      XElement                   lastFoundElement,
                                      AdditionalText             file,
                                      CompilationAnalysisContext context)
        {
            var attributeValue = element?.Attribute(attributeName);
            var value = attributeValue?.Value ?? defaultValue;

            var v = value.Trim();
            if (isGoodValue(v))
                return v;

            var lineInfo   = (IXmlLineInfo)lastFoundElement;
            int lineNumber = lastFoundElement != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
            context.ReportDiagnostic(ExternalDiagnostic.Create(diagnosticDescriptor,
                                                               file.Path,
                                                               lineNumber,
                                                               lastFoundElement.ToStringStartElement()));

            return v;
        }

        private void CheckMainConfigAndLocations(string                     attribute,
                                                 string                     defaultValue,
                                                 Func<string, bool>         isGoodValue,
                                                 DiagnosticDescriptor       diagnosticDescriptor,
                                                 XElement                   systemWeb,
                                                 XElement                   lastFoundElement,
                                                 string                     subElement,
                                                 XDocument                  doc,
                                                 AdditionalText             file,
                                                 CompilationAnalysisContext context)
        {
            var subElementNode = GetElement(systemWeb, ref lastFoundElement, subElement);
            var value = CheckAttribute(subElementNode,
                                       attribute,
                                       defaultValue,
                                       isGoodValue,
                                       diagnosticDescriptor,
                                       lastFoundElement,
                                       file,
                                       context);

            // if the value is bad in the main config element, don't report it if is set to bad again in every location
            if (!isGoodValue(value))
                return;

            var locations = doc.Element("configuration")?.Elements("location");
            if (locations == null)
                return;

            foreach (var location in locations)
            {
                lastFoundElement = location;
                var pages = GetElement(location, ref lastFoundElement, "system.web", subElement);
                CheckAttribute(pages,
                               attribute,
                               value,
                               isGoodValue,
                               diagnosticDescriptor,
                               lastFoundElement,
                               file,
                               context);
            }
        }

        private XElement GetElement(XElement element, ref XElement lastFoundElement, params string[] elements)
        {
            if (element == null)
                return null;

            foreach (var e in elements)
            {
                var el = element.Element(e);
                if (el == null)
                    return null;

                lastFoundElement = el;
                element = lastFoundElement;
            }

            return lastFoundElement;
        }

        public void AnalyzeFile(AdditionalText file, CompilationAnalysisContext context)
        {
            var doc = XDocument.Load(file.Path, LoadOptions.SetLineInfo);
            var config = doc.Element("configuration");
            if (config == null)
                return;

            var lastFoundElement = config;
            var systemWeb = GetElement(config, ref lastFoundElement, "system.web");

            CheckMainConfigAndLocations("validateRequest",
                                        "True",
                                        v => 0 == String.Compare("true", v, StringComparison.OrdinalIgnoreCase),
                                        RuleValidateRequest,
                                        systemWeb,
                                        lastFoundElement,
                                        "pages",
                                        doc,
                                        file,
                                        context);

            CheckMainConfigAndLocations("requestValidationMode",
                                        "4.0",
                                        v =>
                                        {
                                            if (!decimal.TryParse(v, out var version))
                                                return true;

                                            return version >= 4.0M;
                                        },
                                        RuleRequestValidationMode,
                                        systemWeb,
                                        lastFoundElement,
                                        "httpRuntime",
                                        doc,
                                        file,
                                        context);

            CheckMainConfigAndLocations("enableEventValidation",
                                        "True",
                                        v => 0 == String.Compare("true", v, StringComparison.OrdinalIgnoreCase),
                                        RuleEnableEventValidation,
                                        systemWeb,
                                        lastFoundElement,
                                        "pages",
                                        doc,
                                        file,
                                        context);

            CheckMainConfigAndLocations("viewStateEncryptionMode",
                                        "Auto",
                                        v => 0 == String.Compare("Always", v, StringComparison.OrdinalIgnoreCase),
                                        RuleViewStateEncryptionMode,
                                        systemWeb,
                                        lastFoundElement,
                                        "pages",
                                        doc,
                                        file,
                                        context);

            // https://blogs.msdn.microsoft.com/webdev/2014/09/09/farewell-enableviewstatemac/
            CheckMainConfigAndLocations("enableViewStateMac",
                                        "True",
                                        v => 0 == String.Compare("true", v, StringComparison.OrdinalIgnoreCase),
                                        RuleEnableViewStateMac,
                                        systemWeb,
                                        lastFoundElement,
                                        "pages",
                                        doc,
                                        file,
                                        context);
        }
    }
}
