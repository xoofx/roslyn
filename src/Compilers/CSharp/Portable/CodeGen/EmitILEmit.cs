// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CodeGen;

namespace Microsoft.CodeAnalysis.CSharp.CodeGen
{
    internal partial class CodeGenerator
    {
        private void EmitILEmitExpression(BoundILEmit ilEmit)
        {
            var opcode = ilEmit.ILInstruction.OpCode;
            if (opcode.HasVariableStackBehavior())
            {
                int stackBehavior;
                if (ilEmit.Bound is BoundCall)
                {
                    var call = (BoundCall)ilEmit.Bound;
                    stackBehavior = GetCallStackBehavior(call);
                }
                else if (ilEmit.Bound is BoundMethodGroup)
                {
                    var call = ((BoundMethodGroup) ilEmit.Bound).Methods[0];
                    stackBehavior = GetCallStackBehavior(call);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported method call [{ilEmit.Bound.GetType()}] for the IL instruction <{ilEmit.ILInstruction.Name}>");
                }
                _builder.EmitOpCode(opcode, stackBehavior);
            }
            else
            {
                // ldloc/stloc ldarg/starg are handle below so we skip them here
                if (ilEmit.ILInstruction.Argument != OpCodeArg.LocalVar 
                    && ilEmit.ILInstruction.Argument != OpCodeArg.ArgumentVar)
                {
                    _builder.EmitOpCode(opcode);
                }
            }

            if (ilEmit.ILInstruction.Argument == OpCodeArg.None)
            {
                return;
            }

            switch (ilEmit.ILInstruction.Argument)
            {
                case OpCodeArg.Target32:
                case OpCodeArg.Target8:
                    _builder.MarkLabel((BoundLabel)ilEmit.Bound);
                    break;
                case OpCodeArg.ElementType:
                case OpCodeArg.TypeToken:
                case OpCodeArg.Class:
                    EmitSymbolToken(((BoundTypeExpression)ilEmit.Bound).Type, ilEmit.Syntax);
                    break;
                case OpCodeArg.LocalVar:
                    if (opcode == ILOpCode.Ldloc && ilEmit.Bound is BoundLocal)
                    {
                        var localDef = _builder.LocalSlotManager.GetLocal(((BoundLocal)ilEmit.Bound).LocalSymbol);
                        _builder.EmitLocalLoad(localDef);
                    }
                    else if (opcode == ILOpCode.Stloc && ilEmit.Bound is BoundLocal)
                    {
                        var localDef = _builder.LocalSlotManager.GetLocal(((BoundLocal)ilEmit.Bound).LocalSymbol);
                        _builder.EmitLocalStore(localDef);
                    }
                    else
                    {
                        throw new NotSupportedException($"The bound type [{ilEmit.Bound.GetType()}] is not supported for the IL instruction <{ilEmit.ILInstruction.Name}>");
                    }
                    break;
                case OpCodeArg.ArgumentVar:
                    if (opcode == ILOpCode.Ldarg && ilEmit.Bound is BoundParameter)
                    {
                        this.EmitParameterLoad((BoundParameter) ilEmit.Bound);
                    }
                    else if (opcode == ILOpCode.Starg && ilEmit.Bound is BoundParameter)
                    {
                        this.EmitParameterStore((BoundParameter)ilEmit.Bound);
                    }
                    else
                    {
                        throw new NotSupportedException($"The bound type [{ilEmit.Bound.GetType()}] is not supported for the IL instruction <{ilEmit.ILInstruction.Name}>");
                    }
                    break;
                case OpCodeArg.Method:
                case OpCodeArg.CallSite:
                    if (ilEmit.Bound is BoundCall)
                    {
                        EmitSymbolToken(((BoundCall)ilEmit.Bound).Method, ilEmit.Syntax, null); // TODO: handle varargs
                    }
                    else if (ilEmit.Bound is BoundMethodGroup)
                    {
                        EmitSymbolToken(((BoundMethodGroup) ilEmit.Bound).Methods[0], ilEmit.Syntax, null); // TODO: handle varargs
                    }
                    else
                    {
                        throw new NotSupportedException($"The bound type [{ilEmit.Bound.GetType()}] is not supported for the IL instruction <{ilEmit.ILInstruction.Name}>");
                    }
                    break;
                case OpCodeArg.Field:
                    EmitSymbolToken(((BoundFieldAccess)ilEmit.Bound).FieldSymbol, ilEmit.Syntax);
                    break;
                case OpCodeArg.Token:
                    var typeExpression = ilEmit.Bound as BoundTypeExpression;
                    if (typeExpression != null)
                    {
                        EmitSymbolToken(typeExpression.Type, ilEmit.Syntax);
                    }
                    else if (ilEmit.Bound is BoundFieldAccess)
                    {
                        EmitSymbolToken(((BoundFieldAccess)ilEmit.Bound).FieldSymbol, ilEmit.Syntax);
                    }
                    else if (ilEmit.Bound is BoundCall)
                    {
                        EmitSymbolToken(((BoundCall)ilEmit.Bound).Method, ilEmit.Syntax, null); // TODO: handle varargs
                    }
                    else if (ilEmit.Bound is BoundMethodGroup)
                    {
                        EmitSymbolToken(((BoundMethodGroup)ilEmit.Bound).Methods[0], ilEmit.Syntax, null); // TODO: don't handle varargs
                    }
                    else
                    {
                        throw new NotSupportedException($"The bound type [{ilEmit.Bound.GetType()}] is not supported for the IL instruction <{ilEmit.ILInstruction.Name}>");
                    }
                    break;
                case OpCodeArg.Constructor:
                    EmitSymbolToken(((BoundObjectCreationExpression)ilEmit.Bound).Constructor, ilEmit.Syntax, null);
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
                case OpCodeArg.String:
                    // TODO: check values
                    _builder.EmitConstantValue(((BoundLiteral)ilEmit.Bound).ConstantValue);
                    break;
                default:
                    throw new NotImplementedException(
                        $"The IL instruction <{ilEmit.ILInstruction.Name}> is not implemented");
            }
        }
    }
}