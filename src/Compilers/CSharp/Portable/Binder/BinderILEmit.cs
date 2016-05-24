// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.CodeAnalysis.CSharp
{
    internal partial class Binder
    {
        private bool TryCompilerIntrinsic(InvocationExpressionSyntax node, BoundExpression methodCall, DiagnosticBag diagnostics, out BoundExpression result)
        {
            result = null;
            // We expect only a BoundCall
            var call = methodCall as BoundMethodGroup;
            if (call == null)
            {
                return false;
            }

            MethodSymbol method = null;

            var generic = node.Expression as GenericNameSyntax;
            if (generic != null && generic.TypeArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            foreach (var checkMethod in call.Methods)
            {
                var sourceMethodSymbol = checkMethod as SourceMethodSymbol;
                // Check that the method signature of the epecting method:
                // [CompilerIntrinsic]
                // extern void xxxx(string arg);
                if (sourceMethodSymbol != null && sourceMethodSymbol.IsCompilerIntrinsic && (checkMethod.ReturnsVoid || (generic != null && checkMethod.TypeArguments.Length == 1 && checkMethod.ReturnType == checkMethod.TypeArguments[0])))
                {
                    method = checkMethod;
                    break;
                }
            }

            if (method == null)
            {
                return false;
            }

            if (node.ArgumentList.Arguments.Count < 1)
            {
                diagnostics.Add(ErrorCode.ERR_BadArgCount, node.Expression.Location);
                return false;
            }

            var arg0 = node.ArgumentList.Arguments[0];
            var ilName = arg0.ToString().TrimStart('@'); // remove @ for keyword arguments

            // Get IL instruction
            var inst = ILInstruction.Get(ilName);
            if (inst == null)
            {
                diagnostics.Add(ErrorCode.ERR_ILInvalidInstruction, arg0.Location);
                return false;
            }

            var expectedArgCount = inst.Argument == OpCodeArg.None ? 1 : 2;
            // Add more checks
            if (node.ArgumentList.Arguments.Count != expectedArgCount)
            {
                diagnostics.Add(ErrorCode.ERR_BadArgCount, node.Expression.Location);
                return false;
            }

            BoundExpression bound = null;
            // Check argument
            if (inst.Argument != OpCodeArg.None)
            {
                var arg1 = node.ArgumentList.Arguments[1];

                var errCode = ErrorCode.ERR_ArgsInvalid;

                // Perform various checks here
                switch (inst.Argument)
                {
                    case OpCodeArg.Target32:
                        bound = this.BindLabel(arg1.Expression, diagnostics) as BoundLabel;
                        if (bound == null)
                        {
                            errCode = ErrorCode.ERR_LabelNotFound;
                        }
                        break;
                    case OpCodeArg.Target8:
                        bound = this.BindLabel(arg1.Expression, diagnostics) as BoundLabel;
                        if (bound == null)
                        {
                            errCode = ErrorCode.ERR_LabelNotFound;
                        }
                        break;
                    case OpCodeArg.Method:
                    case OpCodeArg.CallSite:
                        {
                            var methodBound = this.BindExpression(arg1.Expression, diagnostics);
                        if (methodBound == null || (!(methodBound is BoundCall) && !(methodBound is BoundMethodGroup)))
                        {
                            // TODO: log proper error
                            errCode = ErrorCode.ERR_MethodNameExpected;
                        }
                        else
                        {
                            bound = methodBound;
                        }
                        break;
                    }

                    case OpCodeArg.ElementType:
                    case OpCodeArg.TypeToken:
                    case OpCodeArg.Class:
                        bound = this.BindExpression(arg1.Expression, diagnostics) as BoundTypeExpression;
                        if (bound == null)
                        {
                            errCode = ErrorCode.ERR_TypeExpected;
                        }
                        break;

                    case OpCodeArg.Field:
                        bound = BindExpression(arg1.Expression, diagnostics) as BoundFieldAccess;
                        if (bound == null)
                        {
                            // TODO: log proper error
                            errCode = ErrorCode.ERR_BadAccess;
                        }
                        break;
                    case OpCodeArg.String:
                        bound = BindExpression(arg1.Expression, diagnostics) as BoundLiteral;
                        if (bound == null || !bound.ConstantValue.IsString)
                        {
                            // TODO: log proper error
                            bound = null;
                            errCode = ErrorCode.ERR_ExpectedVerbatimLiteral;
                        }
                        break;
                    case OpCodeArg.Token:
                        var tokenExpression = BindExpression(arg1.Expression, diagnostics);

                        if (tokenExpression is BoundTypeExpression || tokenExpression is BoundFieldAccess || tokenExpression is BoundCall || (tokenExpression is BoundMethodGroup && ((BoundMethodGroup)tokenExpression).Methods.Length == 1))
                        {
                            bound = tokenExpression;
                        }
                        else
                        {
                            // TODO: log proper error
                            errCode = ErrorCode.ERR_ArgsInvalid;
                        }
                        break;
                    case OpCodeArg.Constructor:
                        bound = BindExpression(arg1.Expression, diagnostics) as BoundObjectCreationExpression;
                        if (bound == null)
                        {
                            // TODO: log proper error
                            errCode = ErrorCode.ERR_ArgsInvalid;
                        }
                        break;
                    case OpCodeArg.ArgumentVar:
                        bound = BindExpression(arg1.Expression, diagnostics) as BoundParameter;
                        if (bound == null)
                        {
                            // TODO: log proper error
                            errCode = ErrorCode.ERR_ArgsInvalid;
                        }
                        break;
                    case OpCodeArg.LocalVar:
                        bound = BindExpression(arg1.Expression, diagnostics) as BoundLocal;
                        if (bound == null)
                        {
                            // TODO: log proper error
                            errCode = ErrorCode.ERR_ArgsInvalid;
                        }
                        break;
                    case OpCodeArg.Int8:
                    case OpCodeArg.UInt8:
                    case OpCodeArg.UInt16:
                    case OpCodeArg.Int32:
                    case OpCodeArg.UInt32:
                    case OpCodeArg.Int64:
                    case OpCodeArg.UInt64:
                    case OpCodeArg.Float32:
                    case OpCodeArg.Float64:
                        // TODO: Handle correctly arguments
                        bound = BindExpression(arg1.Expression, diagnostics) as BoundLiteral;
                        if (bound == null || (!bound.ConstantValue.IsNumeric && !bound.ConstantValue.IsFloating))
                        {
                            // TODO: log proper error
                            bound = null;
                            errCode = ErrorCode.ERR_ArgsInvalid;
                        }
                        break;
                }

                // If we have not been able to bind anything, we have an error
                if (bound == null)
                {
                    diagnostics.Add(errCode, arg1.Location);
                    return false;
                }
            }
            // Emit BoundILEmit here
            TypeSymbol emitType = Compilation.GetSpecialType(SpecialType.System_Void);
            var genericType = generic != null ? generic.TypeArgumentList.Arguments[0] : null;

            if (genericType != null)
            {
                emitType = BindType(genericType, diagnostics);
            }

            result = new BoundILEmit(call.Syntax, emitType)
            {
                ILInstruction = inst,
                Bound = bound
            };
            return true;
        }
    }
}