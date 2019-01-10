﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using SecurityCodeScan.Analyzers.Locale;
using SecurityCodeScan.Analyzers.Utils;
using SecurityCodeScan.Config;

namespace SecurityCodeScan.Analyzers.Taint
{
    internal class VbCodeEvaluation
    {
        public static List<TaintAnalyzerExtensionVisualBasic> Extensions { get; set; } = new List<TaintAnalyzerExtensionVisualBasic>();

        private Configuration ProjectConfiguration;

        private SyntaxNodeHelper SyntaxNodeHelper;

        public VbCodeEvaluation(SyntaxNodeHelper syntaxHelper, Configuration projectConfiguration)
        {
            SyntaxNodeHelper     = syntaxHelper;
            ProjectConfiguration = projectConfiguration;
        }

        public void VisitMethods(SyntaxNodeAnalysisContext ctx)
        {
            try
            {
                var state = new ExecutionState(ctx);

                foreach (var ext in Extensions)
                {
                    ext.VisitBegin(ctx.Node, state);
                }

                VisitNode(ctx.Node, state);

                foreach (var ext in Extensions)
                {
                    ext.VisitEnd(ctx.Node, state);
                }
            }
            catch (Exception e)
            {
                //Intercept the exception for logging. Otherwise, the analyzer fails silently.
                string errorMsg = $"Unhandled exception while visiting method {ctx.Node}\n{e.Message}";
                Logger.Log(errorMsg);
                if (e.InnerException != null)
                    Logger.Log($"{e.InnerException.Message}");
                Logger.Log($"\n{e.StackTrace}", false);
                throw;
            }
        }

        private VariableState VisitBlock(MethodBlockBaseSyntax node, ExecutionState state)
        {
            var lastState = new VariableState(node, VariableTaint.Unknown);
            return VisitStatements(node.Statements, state, lastState);
        }

        private VariableState VisitStatements(SyntaxList<StatementSyntax> statements, ExecutionState state, VariableState lastState)
        {
            foreach (StatementSyntax statement in statements)
            {
                var statementState = VisitNode(statement, state);
                lastState = statementState;

                foreach (var ext in Extensions)
                {
                    ext.VisitStatement(statement, state);
                }
            }

            return lastState;
        }

        private void TaintParameters(MethodBlockBaseSyntax node, ParameterListSyntax parameterList, ExecutionState state)
        {
            foreach (ParameterSyntax parameter in parameterList.Parameters)
            {
                state.AddNewValue(ResolveIdentifier(parameter.Identifier.Identifier),
                                  new VariableState(parameter, VariableTaint.Tainted));
            }
        }

        /// <summary>
        /// Entry point that visits the method statements.
        /// </summary>
        private VariableState VisitMethodDeclaration(MethodBlockBaseSyntax node, ParameterListSyntax parameterList, ExecutionState state)
        {
            if (ProjectConfiguration.AuditMode)
            {
                TaintParameters(node, parameterList, state);
            }
            else
            {
                var symbol = state.AnalysisContext.SemanticModel.GetDeclaredSymbol(node);
                if (symbol != null)
                {
                    if (symbol.IsTaintEntryPoint(ProjectConfiguration.TaintEntryPoints))
                        TaintParameters(node, parameterList, state);
                }
            }

            return VisitBlock(node, state);
        }

        private VariableState VisitForEach(ForEachStatementSyntax node, ExecutionState state)
        {
            var variableState = VisitExpression(node.Expression, state);

            switch (node.ControlVariable)
            {
                case VariableDeclaratorSyntax variableDeclarator:
                    var names = variableDeclarator.Names;
                    foreach (var name in names)
                    {
                        state.AddNewValue(ResolveIdentifier(name.Identifier), variableState);
                    }

                    break;
                case IdentifierNameSyntax identifierName:
                    state.AddNewValue(ResolveIdentifier(identifierName.Identifier), variableState);
                    break;
                default:
                    throw new ArgumentException(nameof(node.ControlVariable));
            }

            return VisitNode(node.Expression, state);
        }

        /// <summary>
        /// Statement are all segment separate by semi-colon.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="state"></param>
        private VariableState VisitNode(SyntaxNode node, ExecutionState state)
        {
            switch (node)
            {
                case UnaryExpressionSyntax unaryExpressionSyntax:
                    return VisitNode(unaryExpressionSyntax.Operand, state);
                case LocalDeclarationStatementSyntax localDeclaration:
                    return VisitLocalDeclaration(localDeclaration, state);
                case VariableDeclaratorSyntax variableDeclaration:
                    return VisitVariableDeclaration(variableDeclaration, state);
                case AssignmentStatementSyntax assignment:
                    return VisitAssignmentStatement(assignment, state);
                case ExpressionStatementSyntax expressionStatement:
                    return VisitExpressionStatement(expressionStatement, state);
                case ExpressionSyntax expression:
                    return VisitExpression(expression, state);
                case MethodBlockSyntax methodBlock:
                    return VisitMethodDeclaration(methodBlock, methodBlock.SubOrFunctionStatement.ParameterList, state);
                case ConstructorBlockSyntax constructorBlockSyntax:
                    return VisitMethodDeclaration(constructorBlockSyntax, constructorBlockSyntax.SubNewStatement.ParameterList, state);
                case PropertyBlockSyntax propertyBlockSyntax:
                    return VisitPropertyBlock(propertyBlockSyntax, state);
                case ReturnStatementSyntax returnStatementSyntax:
                    if (returnStatementSyntax.Expression == null)
                        return new VariableState(node, VariableTaint.Unknown);

                    return VisitExpression(returnStatementSyntax.Expression, state);
                case ForEachStatementSyntax forEachSyntax:
                    return VisitForEach(forEachSyntax, state);
                case FromClauseSyntax fromClauseSyntax:
                    return VisitFromClause(fromClauseSyntax, state);
                case WhereClauseSyntax whereClauseSyntax:
                    return VisitExpression(whereClauseSyntax.Condition, state);
                case SelectClauseSyntax selectClauseSyntax:
                    return VisitSelectClause(selectClauseSyntax, state);
                case ExpressionRangeVariableSyntax expressionRangeVariableSyntax:
                    return VisitExpression(expressionRangeVariableSyntax.Expression, state);
                case CollectionRangeVariableSyntax collectionRangeVariableSyntax:
                        return VisitCollectionRangeVariable(collectionRangeVariableSyntax, state);
                case SingleLineIfStatementSyntax singleLineIfStatementSyntax:
                    return VisitSingleLineIfStatement(singleLineIfStatementSyntax, state);
                case IfStatementSyntax ifStatementSyntax:
                    return VisitExpression(ifStatementSyntax.Condition, state);
                case ElseBlockSyntax elseBlockSyntax:
                {
                    var lastState = new VariableState(elseBlockSyntax, VariableTaint.Unset);
                    return VisitStatements(elseBlockSyntax.Statements, state, lastState);
                }
                case ElseIfStatementSyntax elseIfStatementSyntax:
                    return VisitExpression(elseIfStatementSyntax.Condition, state);
                case ElseIfBlockSyntax elseIfBlockSyntax:
                {
                    var lastState = VisitNode(elseIfBlockSyntax.ElseIfStatement, state);
                    return VisitStatements(elseIfBlockSyntax.Statements, state, lastState);
                }
                case MultiLineIfBlockSyntax multiLineIfBlockSyntax:
                    return VisitMultiLineIfBlock(multiLineIfBlockSyntax, state);
                case SelectBlockSyntax selectBlockSyntax:
                    return VisitSelectBlock(selectBlockSyntax, state);
                case SelectStatementSyntax selectStatementSyntax:
                    return VisitExpression(selectStatementSyntax.Expression, state);
                case CaseBlockSyntax caseBlockSyntax:
                    return VisitStatements(caseBlockSyntax.Statements, state, new VariableState(caseBlockSyntax, VariableTaint.Unset));
            }

            foreach (var n in node.ChildNodes())
            {
                VisitNode(n, state);
            }

            var isBlockStatement = node is ForStatementSyntax ||
                                   node is UsingStatementSyntax;

            if (!isBlockStatement)
            {
#if DEBUG
                //throw new Exception("Unsupported statement " + node.GetType() + " (" + node + ")");
                Logger.Log("Unsupported statement " + node.GetType() + " (" + node + ")");
#endif
            }

            return new VariableState(node, VariableTaint.Unknown);
        }

        private VariableState VisitSelectClause(SelectClauseSyntax selectClauseSyntax, ExecutionState state)
        {
            var finalState = new VariableState(selectClauseSyntax, VariableTaint.Unset);
            foreach (var variable in selectClauseSyntax.Variables)
            {
                finalState.MergeTaint(VisitNode(variable, state).Taint);
            }

            return finalState;
        }

        private VariableState VisitFromClause(FromClauseSyntax fromClauseSyntax, ExecutionState state)
        {
            var finalState = new VariableState(fromClauseSyntax, VariableTaint.Unset);
            foreach (var variable in fromClauseSyntax.Variables)
            {
                finalState.MergeTaint(VisitNode(variable, state).Taint);
            }

            return finalState;
        }

        private VariableState VisitPropertyBlock(PropertyBlockSyntax propertyBlockSyntax, ExecutionState state)
        {
            foreach (var accessor in propertyBlockSyntax.Accessors)
            {
                VisitBlock(accessor, state);
            }

            return new VariableState(propertyBlockSyntax, VariableTaint.Unknown);
        }

        private VariableState VisitAssignmentStatement(AssignmentStatementSyntax assignment, ExecutionState state)
        {
            if (assignment.Kind() != SyntaxKind.SimpleAssignmentStatement)
            {
                var left            = VisitExpression(assignment.Left, state);
                var assignmentState = VisitAssignment(assignment, assignment.Left, assignment.Right, state);
                left.MergeTaint(assignmentState.Taint);
                return left;
            }
            else
            {
                var assignmentState = VisitAssignment(assignment, assignment.Left, assignment.Right, state);
                return MergeVariableState(assignment.Left, assignmentState, state);
            }
        }

        private VariableState VisitCollectionRangeVariable(CollectionRangeVariableSyntax collectionRangeVariableSyntax, ExecutionState state)
        {
            var expressionState = VisitExpression(collectionRangeVariableSyntax.Expression, state);
            var fromSymbol      = SyntaxNodeHelper.GetSymbol(collectionRangeVariableSyntax.Expression, state.AnalysisContext.SemanticModel);
            if (fromSymbol != null)
            {
                switch (fromSymbol)
                {
                    case IPropertySymbol propertyFromSymbol when propertyFromSymbol.Type.IsTaintType(ProjectConfiguration.Behavior):
                    case IFieldSymbol fieldFromSymbol when fieldFromSymbol.Type.IsTaintType(ProjectConfiguration.Behavior):
                        expressionState = new VariableState(collectionRangeVariableSyntax, VariableTaint.Tainted);
                        break;
                }
            }

            state.AddNewValue(ResolveIdentifier(collectionRangeVariableSyntax.Identifier.Identifier), expressionState);
            return expressionState;
        }

        private VariableState VisitSelectBlock(SelectBlockSyntax selectBlockSyntax, ExecutionState state)
        {
            var exprVarState = VisitNode(selectBlockSyntax.SelectStatement, state);
            if (selectBlockSyntax.CaseBlocks.Count <= 0)
                return exprVarState;

            var firstCaseState  = new ExecutionState(state);
            var sectionVarState = VisitNode(selectBlockSyntax.CaseBlocks[0], firstCaseState);
            exprVarState.MergeTaint(sectionVarState.Taint);

            for (var i = 1; i < selectBlockSyntax.CaseBlocks.Count; i++)
            {
                var section   = selectBlockSyntax.CaseBlocks[i];
                var caseState = new ExecutionState(state);
                sectionVarState = VisitNode(section, caseState);
                exprVarState.MergeTaint(sectionVarState.Taint);
                firstCaseState.Merge(caseState);
            }

            if (selectBlockSyntax.CaseBlocks.Any(section => section.Kind() == SyntaxKind.CaseElseBlock))
                state.Replace(firstCaseState);
            else
                state.Merge(firstCaseState);

            return exprVarState;
        }

        private VariableState VisitSingleLineIfStatement(SingleLineIfStatementSyntax singleLineIfStatementSyntax, ExecutionState state)
        {
            var condition = VisitExpression(singleLineIfStatementSyntax.Condition, state);

            var ifState   = new ExecutionState(state);
            var lastState = new VariableState(singleLineIfStatementSyntax, VariableTaint.Unset);
            lastState = VisitStatements(singleLineIfStatementSyntax.Statements, ifState, lastState);
            condition.MergeTaint(lastState.Taint);
            state.Merge(ifState);
            return condition;
        }

        private VariableState VisitMultiLineIfBlock(MultiLineIfBlockSyntax multiLineIfBlockSyntax, ExecutionState state)
        {
            var condition = VisitNode(multiLineIfBlockSyntax.IfStatement, state);

            var ifState     = new ExecutionState(state);
            var lastState   = new VariableState(multiLineIfBlockSyntax, VariableTaint.Unset);
            var ifStatement = VisitStatements(multiLineIfBlockSyntax.Statements, ifState, lastState);
            condition.MergeTaint(ifStatement.Taint);

            foreach (var elseIfBlock in multiLineIfBlockSyntax.ElseIfBlocks)
            {
                var elseState = new ExecutionState(state);
                condition.MergeTaint(VisitNode(elseIfBlock, elseState).Taint);
                ifState.Merge(elseState);
            }

            if (multiLineIfBlockSyntax.ElseBlock != null)
            {
                var elseState     = new ExecutionState(state);
                var elseStatement = VisitNode(multiLineIfBlockSyntax.ElseBlock, elseState);
                condition.MergeTaint(elseStatement.Taint);

                ifState.Merge(elseState);
                state.Replace(ifState);
                return condition;
            }

            state.Merge(ifState);
            return condition;
        }

        /// <summary>
        /// Unwrap
        /// </summary>
        /// <param name="declaration"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        private VariableState VisitLocalDeclaration(LocalDeclarationStatementSyntax declaration, ExecutionState state)
        {
            var finalState = new VariableState(declaration, VariableTaint.Unset);

            foreach (var i in declaration.Declarators)
            {
                finalState.MergeTaint(VisitVariableDeclaration(i, state).Taint);
            }

            return finalState;
        }

        /// <summary>
        /// Evaluate expression that contains a list of assignment.
        /// </summary>
        /// <param name="declaration"></param>
        /// <param name="state"></param>
        private VariableState VisitVariableDeclaration(VariableDeclaratorSyntax declaration, ExecutionState state)
            {
            var lastState = new VariableState(declaration, VariableTaint.Unknown);

            foreach (var variable in declaration.Names)
            {
                VariableState varState;
                if (declaration.Initializer != null)
                {
                    varState = VisitExpression(declaration.Initializer.Value, state);
                    var type = state.AnalysisContext.SemanticModel.GetTypeInfo(declaration.Initializer.Value);

                    if (type.ConvertedType != null && (type.ConvertedType.IsType("System.String") || type.ConvertedType.IsValueType))
                    {
                        var copy = new VariableState(varState.Node, varState.Taint, varState.Value);
                        foreach (var property in varState.PropertyStates)
                        {
                            copy.AddProperty(property.Key, property.Value);
                        }

                        varState = copy;
                    }
                }
                else if (declaration.AsClause is AsNewClauseSyntax asNewClauseSyntax)
                {
                    varState = VisitExpression(asNewClauseSyntax.NewExpression, state);
                }
                else
                {
                    varState = new VariableState(variable, VariableTaint.Constant);
                }

                state.AddNewValue(ResolveIdentifier(variable.Identifier), varState);
                lastState = varState;
            }

            return lastState;
        }

        private VariableState VisitExpression(ExpressionSyntax expression, ExecutionState state)
        {
            // TODO: Review other expression types that are unique to VB. 
            // TODO: Write tests to cover all these.

            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesizedExpressionSyntax:
                    return VisitExpression(parenthesizedExpressionSyntax.Expression, state);
                case InvocationExpressionSyntax invocationExpressionSyntax:
                    return VisitMethodInvocation(invocationExpressionSyntax, state);
                case ObjectCreationExpressionSyntax objectCreationExpressionSyntax:
                    return VisitObjectCreation(objectCreationExpressionSyntax, state);
                case LiteralExpressionSyntax literalExpressionSyntax:
                    return new VariableState(literalExpressionSyntax, VariableTaint.Constant, literalExpressionSyntax.Token.Value);
                case IdentifierNameSyntax identifierNameSyntax:
                    return VisitIdentifierName(identifierNameSyntax, state);
                case BinaryExpressionSyntax binaryExpressionSyntax:
                    return VisitBinaryExpression(binaryExpressionSyntax, binaryExpressionSyntax.Left, binaryExpressionSyntax.Right, state);
                case BinaryConditionalExpressionSyntax binaryConditionalExpressionSyntax:
                    return VisitBinaryExpression(binaryConditionalExpressionSyntax,
                                                 binaryConditionalExpressionSyntax.FirstExpression,
                                                 binaryConditionalExpressionSyntax.SecondExpression, state);
                case MemberAccessExpressionSyntax memberAccessExpressionSyntax:
                    return VisitMemberAccessExpression(memberAccessExpressionSyntax, state);
                case ArrayCreationExpressionSyntax arrayCreationExpressionSyntax:
                    return VisitArrayCreation(arrayCreationExpressionSyntax, arrayCreationExpressionSyntax.Initializer, state);
                case CollectionInitializerSyntax collectionInitializerSyntax:
                    return VisitArrayCreation(collectionInitializerSyntax, collectionInitializerSyntax, state);
                case TypeOfExpressionSyntax typeOfExpressionSyntax:
                    return new VariableState(typeOfExpressionSyntax, VariableTaint.Safe);
                case GetTypeExpressionSyntax getTypeExpressionSyntax:
                    return new VariableState(getTypeExpressionSyntax, VariableTaint.Safe);
                case TernaryConditionalExpressionSyntax ternaryConditionalExpressionSyntax:
                {
                    VisitExpression(ternaryConditionalExpressionSyntax.Condition, state);
                    var finalState = new VariableState(ternaryConditionalExpressionSyntax, VariableTaint.Unset);

                    var whenTrueState = VisitExpression(ternaryConditionalExpressionSyntax.WhenTrue, state);
                    finalState.MergeTaint(whenTrueState.Taint);
                    var whenFalseState = VisitExpression(ternaryConditionalExpressionSyntax.WhenFalse, state);
                    finalState.MergeTaint(whenFalseState.Taint);

                    return finalState;
                }
                case QueryExpressionSyntax queryExpressionSyntax:
                {
                    var finalState = new VariableState(queryExpressionSyntax, VariableTaint.Unset);
                    foreach (var clause in queryExpressionSyntax.Clauses)
                    {
                        finalState.MergeTaint(VisitNode(clause, state).Taint);
                    }

                    return finalState;
                }
                case InterpolatedStringExpressionSyntax interpolatedStringExpressionSyntax:
                    return VisitInterpolatedString(interpolatedStringExpressionSyntax, state);
                case DirectCastExpressionSyntax directCastExpressionSyntax:
                    return VisitExpression(directCastExpressionSyntax.Expression, state);
                case CTypeExpressionSyntax cTypeExpressionSyntax:
                    return VisitExpression(cTypeExpressionSyntax.Expression, state);
                case UnaryExpressionSyntax unaryExpressionSyntax:
                    return VisitExpression(unaryExpressionSyntax.Operand, state);
            }

#if DEBUG
            Logger.Log("Unsupported expression " + expression.GetType() + " (" + expression + ")");
#endif
            return new VariableState(expression, VariableTaint.Unknown);
        }

        private VariableState VisitInterpolatedString(InterpolatedStringExpressionSyntax interpolatedString,
                                                      ExecutionState                     state)
        {
            var varState = new VariableState(interpolatedString, VariableTaint.Constant);

            foreach (var content in interpolatedString.Contents)
            {
                if (content is InterpolatedStringTextSyntax)
                {
                    varState.MergeTaint(VariableTaint.Constant);
                }

                if (!(content is InterpolationSyntax interpolation))
                    continue;

                var expressionState = VisitExpression(interpolation.Expression, state);
                varState.MergeTaint(expressionState.Taint);
            }

            return varState;
        }

        private VariableState VisitMethodInvocation(InvocationExpressionSyntax node, ExecutionState state)
        {
            VariableState memberVariableState = null;
            if (node.Expression is MemberAccessExpressionSyntax memberAccessExpression)
            {
                if (memberAccessExpression.Expression != null)
                {
                    memberVariableState = VisitExpression(memberAccessExpression.Expression, state);
                }
                else
                {
                    var with = memberAccessExpression.AncestorsAndSelf().OfType<WithBlockSyntax>().First();
                    memberVariableState = VisitExpression(with.WithStatement.Expression, state);
                }

                var taintSourceState = CheckIfTaintSource(memberAccessExpression, state);
                if (taintSourceState != null)
                    memberVariableState.MergeTaint(taintSourceState.Taint);
            }

            return VisitInvocationAndCreation(node, node.ArgumentList, state, memberVariableState?.Taint, memberVariableState);
        }

        private string GetMethodName(ExpressionSyntax node)
        {
            string methodName;
            switch (node)
            {
                case ObjectCreationExpressionSyntax objectCreationExpressionSyntax:
                    methodName = $"{objectCreationExpressionSyntax.NewKeyword} {objectCreationExpressionSyntax.Type}";
                    break;
                case InvocationExpressionSyntax invocationExpressionSyntax:
                    methodName = invocationExpressionSyntax.Expression.ToString();
                    break;
                default:
                    methodName = node.ToString();
                    break;
            }

            return methodName;
        }

        private IReadOnlyDictionary<int, PostCondition> GetPostConditions(MethodBehavior behavior, bool isExtensionMethod, ArgumentListSyntax argList, ExecutionState state)
        {
            if (behavior.Conditions == null)
                return behavior.PostConditions;

            foreach (var condition in behavior.Conditions)
            {
                if (CheckPrecondition(condition.If, isExtensionMethod, argList, state))
                    return condition.Then;
            }

            return behavior.PostConditions;
        }

        private bool CheckPrecondition(IReadOnlyDictionary<int, object> condition, bool isExtensionMethod, ArgumentListSyntax argList, ExecutionState state)
        {
            for (var i = 0; i < argList?.Arguments.Count; i++)
            {
                var argument            = argList.Arguments[i];
                var adjustedArgumentIdx = isExtensionMethod ? i + 1 : i;

                if (!condition.TryGetValue(adjustedArgumentIdx, out var preconditionArgumentValue))
                {
                    continue;
                }

                var calculatedArgumentValue = state.AnalysisContext.SemanticModel.GetConstantValue(argument.GetExpression());
                if (calculatedArgumentValue.HasValue && calculatedArgumentValue.Value.Equals(preconditionArgumentValue))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Logic for each method invocation (including constructor)
        /// The argument list is required because <code>InvocationExpressionSyntax</code> and 
        /// <code>ObjectCreationExpressionSyntax</code> do not share a common interface.
        /// </summary>
        private VariableState VisitInvocationAndCreation(ExpressionSyntax   node,
                                                         ArgumentListSyntax argList,
                                                         ExecutionState     state,
                                                         VariableTaint?     initialTaint = null,
                                                         VariableState       memberVariableState = null)
        {
            var symbol = state.GetSymbol(node);
            if (symbol == null)
                return new VariableState(node, initialTaint ?? VariableTaint.Unknown);

            var  methodSymbol      = symbol as IMethodSymbol;
            bool isExtensionMethod = methodSymbol?.ReducedFrom != null;
            var  behavior          = symbol.GetMethodBehavior(ProjectConfiguration.Behavior);
            IReadOnlyDictionary<int, PostCondition> postConditions = null;
            if (behavior != null)
                postConditions = GetPostConditions(behavior, isExtensionMethod, argList, state);

            PostCondition returnPostCondition = null;
            postConditions?.TryGetValue(-1, out returnPostCondition);

            VariableState returnState = initialTaint != null && !symbol.IsStatic
                                            ? new VariableState(node, initialTaint.Value)
                                            : new VariableState(node, argList?.Arguments.Count > 0 && behavior != null
                                                                          ? VariableTaint.Unset
                                                                          : VariableTaint.Unknown);

            var argCount = argList?.Arguments.Count;
            var argumentStates = argCount.HasValue &&
                                 argCount.Value > 0 &&
                                 (postConditions?.Any(c => c.Key != -1 && (c.Value.Taint != 0ul || c.Value.TaintFromArguments.Any())) == true ||
                                  methodSymbol != null && methodSymbol.Parameters.Any(x => x.RefKind != RefKind.None))
                                     ? new VariableState[argCount.Value]
                                     : null;

            for (var i = 0; i < argList?.Arguments.Count; i++)
            {
                var argument      = argList.Arguments[i];
                var argumentState = VisitExpression(argument.GetExpression(), state);
                if (argumentStates != null)
                    argumentStates[i] = argumentState;

#if DEBUG
                Logger.Log(symbol.ContainingType + "." + symbol.Name + " -> " + argumentState);
#endif

                var adjustedArgumentIdx = isExtensionMethod ? i + 1 : i;

                if (behavior != null)
                {
                    if ((argumentState.Taint & (ProjectConfiguration.AuditMode
                                                    ? VariableTaint.Tainted | VariableTaint.Unknown
                                                    : VariableTaint.Tainted)) != 0)
                    {
                        //If the current parameter can be injected.
                        if (behavior.InjectableArguments.TryGetValue(adjustedArgumentIdx, out var injectableArgument) &&
                            (injectableArgument.RequiredTaintBits & (ulong)argumentState.Taint) != injectableArgument.RequiredTaintBits)
                        {
                            var newRule    = LocaleUtil.GetDescriptor(injectableArgument.Locale);
                            var diagnostic = Diagnostic.Create(newRule, argument.GetExpression().GetLocation(), GetMethodName(node), (i + 1).ToNthString());
                            state.AnalysisContext.ReportDiagnostic(diagnostic);
                        }
                    }
                    else if (argumentState.Taint == VariableTaint.Constant)
                    {
                        if (behavior.InjectableArguments.TryGetValue(adjustedArgumentIdx, out var injectableArgument) &&
                            injectableArgument.Not                                                                    && (injectableArgument.RequiredTaintBits & (ulong)argumentState.Taint) != 0ul)
                        {
                            var newRule    = LocaleUtil.GetDescriptor(injectableArgument.Locale);
                            var diagnostic = Diagnostic.Create(newRule, argument.GetExpression().GetLocation(), GetMethodName(node), (i + 1).ToNthString());
                            state.AnalysisContext.ReportDiagnostic(diagnostic);
                        }
                    }
                }

                var argumentToSearch = adjustedArgumentIdx;
                if (methodSymbol != null                           &&
                    i            >= methodSymbol.Parameters.Length &&
                    methodSymbol.Parameters[methodSymbol.Parameters.Length - 1].IsParams)
                {
                    argumentToSearch = isExtensionMethod ? methodSymbol.Parameters.Length : methodSymbol.Parameters.Length - 1;
                }

                if (returnPostCondition == null ||
                    returnPostCondition.TaintFromArguments.Contains(argumentToSearch))
                {
                    returnState.MergeTaint(argumentState.Taint);
                }

                //TODO: taint all objects passed as arguments
            }

            if (returnPostCondition != null)
            {
                returnState.ApplyTaint(returnPostCondition.Taint);
            }

            if (argumentStates != null)
            {
                for (var i = 0; i < argList.Arguments.Count; i++)
                {
                    var adjustedPostConditionIdx = isExtensionMethod ? i + 1 : i;

                    if (postConditions != null && postConditions.TryGetValue(adjustedPostConditionIdx, out var postCondition))
                    {
                        foreach (var argIdx in postCondition.TaintFromArguments)
                        {
                            var adjustedArgumentIdx = isExtensionMethod ? argIdx + 1 : argIdx;
                            argumentStates[adjustedPostConditionIdx].MergeTaint(argumentStates[adjustedArgumentIdx].Taint);
                        }

                        argumentStates[adjustedPostConditionIdx].ApplyTaint(postCondition.Taint);
                    }
                    else if (methodSymbol != null)
                    {
                        if (i >= methodSymbol.Parameters.Length)
                        {
                            if (!methodSymbol.Parameters[methodSymbol.Parameters.Length - 1].IsParams)
                                throw new IndexOutOfRangeException();
                        }
                        else if (methodSymbol.Parameters[i].RefKind != RefKind.None)
                        {
                            argumentStates[i].MergeTaint(returnState.Taint);
                        }
                    }
                }
            }

            if (memberVariableState != null &&
                methodSymbol        != null &&
                methodSymbol.ReturnsVoid    &&
                !methodSymbol.IsStatic      &&
                methodSymbol.Parameters.All(x => x.RefKind == RefKind.None))
            {
                memberVariableState.MergeTaint(returnState.Taint);
            }

            //Additional analysis by extension
            foreach (var ext in Extensions)
            {
                ext.VisitInvocationAndCreation(node, argList, state);
            }

            return returnState;
        }

        private VariableState VisitNamedFieldInitializer(NamedFieldInitializerSyntax node, ExecutionState state, VariableState currentScope)
        {
            var assignmentState = VisitAssignment(node, node.Name, node.Expression, state);
            return MergeVariableState(node.Name, assignmentState, state, currentScope);
        }

        private VariableState VisitAssignment(VisualBasicSyntaxNode node,
                                              ExpressionSyntax      leftExpression,
                                              ExpressionSyntax      rightExpression,
                                              ExecutionState        state)
        {
            var            leftSymbol = state.GetSymbol(leftExpression);
            MethodBehavior behavior   = null;
            if (leftSymbol != null)
                behavior = leftSymbol.GetMethodBehavior(ProjectConfiguration.Behavior);

            var variableState = VisitExpression(rightExpression, state);

            //Additional analysis by extension
            foreach (var ext in Extensions)
            {
                ext.VisitAssignment(node, state, behavior, leftSymbol, variableState);
            }

            //if (leftSymbol != null)
            //{
            //    var rightTypeSymbol = state.AnalysisContext.SemanticModel.GetTypeInfo(rightExpression).Type;
            //    if (rightTypeSymbol == null)
            //        return new VariableState(rightExpression, VariableTaint.Unknown);

            //    var leftTypeSymbol = state.AnalysisContext.SemanticModel.GetTypeInfo(leftExpression).Type;
            //    if (!state.AnalysisContext.SemanticModel.Compilation.ClassifyConversion(rightTypeSymbol, leftTypeSymbol).Exists)
            //        return new VariableState(rightExpression, VariableTaint.Unknown);
            //}

            if (variableState.Taint != VariableTaint.Constant &&
                behavior != null &&
                // compare if all required sanitization bits are set
                ((ulong)(variableState.Taint & VariableTaint.Safe) & behavior.InjectableField.RequiredTaintBits) != behavior.InjectableField.RequiredTaintBits &&
                (variableState.Taint & (ProjectConfiguration.AuditMode ? VariableTaint.Tainted | VariableTaint.Unknown : VariableTaint.Tainted)) != 0)
            {
                var newRule    = LocaleUtil.GetDescriptor(behavior.InjectableField.Locale, "title_assignment");
                var diagnostic = Diagnostic.Create(newRule, node.GetLocation());
                state.AnalysisContext.ReportDiagnostic(diagnostic);
            }

            //TODO: taint the variable being assigned.

            return variableState;
        }

        private VariableState VisitObjectCreation(ObjectCreationExpressionSyntax node, ExecutionState state)
        {
            VariableState finalState = VisitInvocationAndCreation(node, node.ArgumentList, state);

            foreach (SyntaxNode child in node.DescendantNodes())
            {
                if (child is NamedFieldInitializerSyntax namedFieldInitializerSyntax)
                {
                    VisitNamedFieldInitializer(namedFieldInitializerSyntax, state, finalState);
                }
                else
                {
#if DEBUG
                    Logger.Log(child.GetText().ToString().Trim() + " -> " + finalState);
#endif
                }
            }

            return finalState;
        }

        /// <summary>
        /// Combine the state of the two operands. Binary expression include concatenation.
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        private VariableState VisitBinaryExpression(ExpressionSyntax expression,
                                                    ExpressionSyntax leftExpression,
                                                    ExpressionSyntax rightExrpession,
                                                    ExecutionState state)
        {
            var result = new VariableState(expression, VariableTaint.Unset);
            var left   = VisitExpression(leftExpression, state);
            result.MergeTaint(left.Taint);
            var right = VisitExpression(rightExrpession, state);
            result.MergeTaint(right.Taint);
            return result;
        }

        /// <summary>
        /// Identifier name include variable name.
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        private VariableState VisitIdentifierName(ExpressionSyntax expression, ExecutionState state)
        {
            var varState = GetVariableState(expression, state);
            if (varState != null)
                return varState;

            var taintSourceState = CheckIfTaintSource(expression, state);
            if (taintSourceState != null)
                return taintSourceState;

            return ResolveVariableState(expression, state);
        }

        private VariableState VisitMemberAccessExpression(MemberAccessExpressionSyntax expression, ExecutionState state)
        {
            var varState = VisitIdentifierName(expression, state);

            if (varState.Taint == VariableTaint.Constant || expression.Expression == null)
            {
                return varState;
            }

            var expressionState = VisitExpression(expression.Expression, state);
            varState.MergeTaint(expressionState.Taint);

            return varState;
        }

        private VariableState CheckIfTaintSource(ExpressionSyntax expression, ExecutionState state)
        {
            var symbol   = state.GetSymbol(expression);
            var behavior = symbol?.GetMethodBehavior(ProjectConfiguration.Behavior);
            if (behavior != null && behavior.PostConditions.TryGetValue(-1, out var taint))
            {
                return new VariableState(expression, (VariableTaint)taint.Taint);
            }

            return null;
        }

        private VariableState ResolveVariableState(ExpressionSyntax          expression,
                                                   ExecutionState            state,
                                                   SemanticModel             semanticModel = null,
                                                   HashSet<ExpressionSyntax> visited       = null)
        {
            semanticModel = semanticModel ?? state.AnalysisContext.SemanticModel;
            var symbol    = semanticModel.GetSymbolInfo(expression).Symbol;
            switch (symbol)
            {
                case null:
                    return new VariableState(expression, VariableTaint.Unknown);
                case IFieldSymbol field:
                    if (field.IsConst)
                        return new VariableState(expression, VariableTaint.Constant);

                    if (!field.IsReadOnly)
                        return new VariableState(expression, VariableTaint.Unknown);

                    if (ProjectConfiguration.ConstantFields.Contains(field.GetTypeName()))
                    {
                        return new VariableState(expression, VariableTaint.Constant);
                    }

                    return new VariableState(expression, VariableTaint.Unknown);
                case IPropertySymbol prop:
                    if (prop.IsVirtual || prop.IsOverride || prop.IsAbstract)
                        return new VariableState(expression, VariableTaint.Unknown);

                    // TODO: Use public API
                    var syntaxNodeProperty = prop.GetMethod.GetType().GetTypeInfo().BaseType.GetTypeInfo().GetDeclaredProperty("Syntax");
                    var syntaxNode         = (VisualBasicSyntaxNode)syntaxNodeProperty?.GetValue(prop.GetMethod);
                    if (syntaxNode == null)
                        return new VariableState(expression, VariableTaint.Unknown);

                    var possiblyOtherSemanticModel = semanticModel.Compilation.GetSemanticModel(syntaxNode.SyntaxTree);

                    if (!(syntaxNode is AccessorBlockSyntax accessorBlockSyntax) || accessorBlockSyntax.Statements.Count  <= 0)
                        return new VariableState(expression, VariableTaint.Unknown);

                    var flow = possiblyOtherSemanticModel.AnalyzeControlFlow(accessorBlockSyntax.Statements.First(),
                                                                             accessorBlockSyntax.Statements.Last());
                    if (flow.Succeeded && AllReturnConstant(flow.ExitPoints, possiblyOtherSemanticModel, visited))
                    {
                        return new VariableState(expression, VariableTaint.Constant);
                    }

                    return new VariableState(expression, VariableTaint.Unknown);
            }

            return new VariableState(expression, VariableTaint.Unknown);
        }

        private bool AllReturnConstant(ImmutableArray<SyntaxNode> exitPoints, SemanticModel semanticModel, HashSet<ExpressionSyntax> visited)
        {
            foreach (var exitPoint in exitPoints)
            {
                if (!(exitPoint is ReturnStatementSyntax returnStatementSyntax))
                    return false;

                if (semanticModel.GetConstantValue(returnStatementSyntax.Expression)
                                 .HasValue)
                {
                    continue;
                }

                if (visited == null)
                    visited = new HashSet<ExpressionSyntax>();
                else if (!visited.Add(returnStatementSyntax.Expression))
                    return false;

                if (ResolveVariableState(returnStatementSyntax.Expression, null, semanticModel, visited).Taint != VariableTaint.Constant)
                    return false;
            }

            return true;
        }

        private VariableState VisitExpressionStatement(ExpressionStatementSyntax node, ExecutionState state)
        {
            return VisitExpression(node.Expression, state); //Simply unwrap the expression
        }

        private VariableState VisitArrayCreation(SyntaxNode node, CollectionInitializerSyntax arrayInit, ExecutionState state)
        {
            var finalState = new VariableState(node, VariableTaint.Safe);
            if (arrayInit == null)
                return finalState;

            foreach (var ex in arrayInit.Initializers)
            {
                var exprState = VisitExpression(ex, state);
                finalState.MergeTaint(exprState.Taint);
            }

            return finalState;
        }

        private VariableState GetVariableState(ExpressionSyntax expression, ExecutionState state)
        {
            if (!(expression is MemberAccessExpressionSyntax memberAccessExpressionSyntax))
            {
                VariableState result;
                if (!(expression is IdentifierNameSyntax identifierNameSyntax))
                {
                    if (expression is MeExpressionSyntax && state.VariableStates.TryGetValue("this", out result))
                        return result;

                    return null;
                }

                var identifier = ResolveIdentifier(identifierNameSyntax.Identifier);
                if (state.VariableStates.TryGetValue(identifier, out result))
                    return result;

                return null;
            }

            var variableState = GetVariableState(memberAccessExpressionSyntax.Expression, state);
            if (variableState == null)
                return null;

            var stateIdentifier = ResolveIdentifier(memberAccessExpressionSyntax.Name.Identifier);
            //make sure this identifier exists
            if (variableState.PropertyStates.TryGetValue(stateIdentifier, out var propertyState))
                return propertyState;

            return null;
        }

        private VariableState MergeVariableState(ExpressionSyntax expression,
                                                 VariableState    newVariableState,
                                                 ExecutionState   state,
                                                 VariableState    currentScope = null)
        {
            var variableStateToMerge = newVariableState ?? new VariableState(expression, VariableTaint.Unset);
            if (!(expression is MemberAccessExpressionSyntax memberAccessExpressionSyntax))
            {
                var identifier = "";
                if (expression is IdentifierNameSyntax identifierNameSyntax)
                    identifier = ResolveIdentifier(identifierNameSyntax.Identifier);
                else if (expression is MeExpressionSyntax)
                    identifier = "this";

                if (currentScope != null)
                {
                    currentScope.AddOrMergeProperty(identifier, variableStateToMerge);
                    return currentScope.PropertyStates[identifier];
                }

                state.AddOrUpdateValue(identifier, variableStateToMerge);
                return state.VariableStates[identifier];
            }

            var variableState = MergeVariableState(memberAccessExpressionSyntax.Expression, null, state, currentScope);

            var stateIdentifier = ResolveIdentifier(memberAccessExpressionSyntax.Name.Identifier);
            //make sure this identifier exists
            variableState.AddOrMergeProperty(stateIdentifier, variableStateToMerge);
            return variableState.PropertyStates[stateIdentifier];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="syntaxToken"></param>
        /// <returns></returns>
        private string ResolveIdentifier(SyntaxToken syntaxToken)
        {
            return syntaxToken.Text;
        }
    }
}
