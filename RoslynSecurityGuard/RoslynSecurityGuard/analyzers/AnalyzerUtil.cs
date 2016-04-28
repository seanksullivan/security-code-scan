﻿using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSecurityGuard
{
    public class AnalyzerUtil
    {

        public static DiagnosticDescriptor GetDescriptorFromResource(Type analyzer, DiagnosticSeverity severity) {
            return new DiagnosticDescriptor(GetLocalString(analyzer.Name + "_Id").ToString(),
                GetLocalString(analyzer.Name + "_Title"),
                GetLocalString(analyzer.Name + "_Message"),
                "Security", 
                severity, 
                isEnabledByDefault: true,
                helpLinkUri : GetLocalString(analyzer.Name + "_Url").ToString(),
                description : GetLocalString(analyzer.Name + "_Description"));
        }

        private static LocalizableString GetLocalString(string id) {
            return new LocalizableResourceString(id, Messages.ResourceManager, typeof(Messages));
        }

        public static bool InvokeMatch(ISymbol symbol, string className = null, string method = null) {
            if (symbol == null) {
                return false; //Code did not compile
            }

            if (className == null && method == null) {
                throw new InvalidOperationException("At least one parameter must be specified (className, methodName, ...)");
            }

            if (className != null && symbol.ContainingType?.Name != className) {
                return false; //Class name does not match
            }
            if (className != null && symbol.Name != method) {
                return false; //Method name does not match
            }
            return true;
        }

        internal static bool ValueIsExternal(DataFlowAnalysis flow, ArgumentSyntax arg) {
            
            return true;
        }

        public static SyntaxNode GetMethodFromNode(SyntaxNode node) {

            SyntaxNode current = node;
            while (current.Parent != null) {
                current = current.Parent;
            }
            return current;
        }
    }
}
