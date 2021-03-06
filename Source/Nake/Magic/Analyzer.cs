﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Roslyn.Compilers.CSharp;

namespace Nake.Magic
{
    class Analyzer : SyntaxWalker
    {
        public readonly AnalysisResult Result = new AnalysisResult();

        readonly SemanticModel model;
        readonly IDictionary<string, string> substitutions;

        Task current;
        bool visitingConstant;

        public Analyzer(SemanticModel model, IDictionary<string, string> substitutions)
        {
            this.model = model;
            this.substitutions = new Dictionary<string, string>(substitutions, new CaseInsensitiveEqualityComparer());
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var symbol = model.GetDeclaredSymbol(node);

            if (!Task.IsAnnotated(symbol))
            {
                base.VisitMethodDeclaration(node);
                return;
            }

            current = Result.Find(symbol);

            if (current == null)
            {
                current = new Task(symbol);
                Result.Add(symbol, current);
            }

            base.VisitMethodDeclaration(node);
            current = null;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = model.GetSymbolInfo(node).Symbol as MethodSymbol;
            
            if (symbol == null)
                return;

            if (!Task.IsAnnotated(symbol))
            {
                base.VisitInvocationExpression(node);
                return;
            }

            var task = Result.Find(symbol);

            if (task == null)
            {
                task = new Task(symbol);                
                Result.Add(symbol, task);
            }
            
            Result.Add(node, new ProxyInvocation(task, node));

            if (current != null)
                current.AddDependency(task);

            base.VisitInvocationExpression(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            visitingConstant = node.Modifiers.Any(SyntaxKind.ConstKeyword);

            foreach (var variable in node.Declaration.Variables)
            {
                var symbol = (FieldSymbol) model.GetDeclaredSymbol(variable);

                var fullName = symbol.ToString();
                if (!substitutions.ContainsKey(fullName))
                    continue;

                if (FieldSubstitution.Qualifies(symbol))
                    Result.Add(variable, new FieldSubstitution(variable, symbol, substitutions[fullName]));
            }

            base.VisitFieldDeclaration(node);
            visitingConstant = false;
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            visitingConstant = node.Modifiers.Any(SyntaxKind.ConstKeyword);
            
            base.VisitLocalDeclarationStatement(node);
            visitingConstant = false;
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            visitingConstant = true;

            base.VisitParameter(node);
            visitingConstant = false;
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            visitingConstant = true;

            base.VisitAttribute(node);
            visitingConstant = false;
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (StringExpansion.Qualifies(node))
                Result.Add(node, new StringExpansion(model, node, visitingConstant));
            
            base.VisitLiteralExpression(node);
        }
    }
}