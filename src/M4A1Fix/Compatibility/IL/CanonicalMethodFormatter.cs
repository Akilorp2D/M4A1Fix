using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace M4A1Fix.Compatibility.IL
{
    // CANONICAL_IL_V1 is a persisted compatibility format: preserve its output bytes.
    internal static class CanonicalMethodFormatter
    {
        internal static bool TryBuildCanonicalMethod(
            MethodInfo method,
            int expectedCodeSize,
            string displayName,
            List<DecodedInstruction> instructions,
            out string canonical,
            out string reason)
        {
            canonical = null;
            reason = null;

            try
            {
                MethodBody body = method.GetMethodBody();
                if (body == null)
                {
                    reason = displayName + " has no method body.";
                    return false;
                }

                var offsetToIndex = new Dictionary<int, int>();
                for (int i = 0; i < instructions.Count; i++)
                    offsetToIndex.Add(instructions[i].Offset, i);
                offsetToIndex.Add(expectedCodeSize, instructions.Count);

                var lines = new List<string>();
                lines.Add("CANONICAL_IL_V1");
                lines.Add("METHOD=" + CanonicalMethodIdentity(method));
                lines.Add("INIT_LOCALS=" + (body.InitLocals ? "1" : "0"));
                lines.Add("MAX_STACK=" + body.MaxStackSize.ToString(CultureInfo.InvariantCulture));

                AppendLocals(body, lines);
                if (!TryAppendInstructions(instructions, offsetToIndex, displayName, lines, out reason) ||
                    !TryAppendExceptionRegions(body, offsetToIndex, displayName, lines, out reason))
                    return false;

                canonical = string.Join("\n", lines.ToArray()) + "\n";
                return true;
            }
            catch (Exception ex)
            {
                reason = displayName + " canonicalization failed closed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static void AppendLocals(MethodBody body, List<string> lines)
        {
            IList<LocalVariableInfo> locals = body.LocalVariables;
            lines.Add("LOCALS=" + locals.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < locals.Count; i++)
            {
                LocalVariableInfo local = locals[i];
                lines.Add(
                    "LOCAL[" + i.ToString(CultureInfo.InvariantCulture) + "]=T=" +
                    CanonicalTypeIdentity(local.LocalType) + ";PINNED=" + (local.IsPinned ? "1" : "0"));
            }
        }

        private static bool TryAppendInstructions(List<DecodedInstruction> instructions,
            Dictionary<int, int> offsetToIndex, string displayName, List<string> lines, out string reason)
        {
            reason = null;
            lines.Add("INSTRUCTIONS=" + instructions.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < instructions.Count; i++)
            {
                DecodedInstruction instruction = instructions[i];
                if (!TryCanonicalOperand(instruction, offsetToIndex, out string operand, out reason))
                {
                    reason = displayName + " canonical operand failed at instruction " +
                             i.ToString(CultureInfo.InvariantCulture) + ": " + reason;
                    return false;
                }

                lines.Add("I[" + i.ToString(CultureInfo.InvariantCulture) + "]=OP=" +
                          instruction.OpCode.Name + ";O=" + operand);
            }

            return true;
        }

        private static bool TryAppendExceptionRegions(MethodBody body,
            Dictionary<int, int> offsetToIndex, string displayName, List<string> lines, out string reason)
        {
            reason = null;
            IList<ExceptionHandlingClause> clauses = body.ExceptionHandlingClauses;
            lines.Add("EH=" + clauses.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < clauses.Count; i++)
            {
                ExceptionHandlingClause clause = clauses[i];
                if (!offsetToIndex.TryGetValue(clause.TryOffset, out int tryStart) ||
                    !offsetToIndex.TryGetValue(clause.TryOffset + clause.TryLength, out int tryEnd) ||
                    !offsetToIndex.TryGetValue(clause.HandlerOffset, out int handlerStart) ||
                    !offsetToIndex.TryGetValue(clause.HandlerOffset + clause.HandlerLength, out int handlerEnd))
                {
                    reason = displayName + " has an EH boundary outside canonical instruction boundaries.";
                    return false;
                }

                string kind;
                string filter = "NONE";
                string catchType = "NONE";
                if (clause.Flags == ExceptionHandlingClauseOptions.Clause)
                {
                    kind = "CATCH";
                    if (clause.CatchType == null)
                    {
                        reason = displayName + " has a catch clause without a catch type.";
                        return false;
                    }
                    catchType = CanonicalTypeIdentity(clause.CatchType);
                }
                else if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                {
                    kind = "FILTER";
                    if (!offsetToIndex.TryGetValue(clause.FilterOffset, out int filterIndex))
                    {
                        reason = displayName + " has a filter boundary outside canonical instruction boundaries.";
                        return false;
                    }
                    filter = filterIndex.ToString(CultureInfo.InvariantCulture);
                }
                else if (clause.Flags == ExceptionHandlingClauseOptions.Finally)
                {
                    kind = "FINALLY";
                }
                else if (clause.Flags == ExceptionHandlingClauseOptions.Fault)
                {
                    kind = "FAULT";
                }
                else
                {
                    reason = displayName + " has an unsupported EH clause kind: " + clause.Flags + ".";
                    return false;
                }

                lines.Add(
                    "EH[" + i.ToString(CultureInfo.InvariantCulture) + "]=K=" + kind +
                    ";TRY=" + tryStart.ToString(CultureInfo.InvariantCulture) + ".." + tryEnd.ToString(CultureInfo.InvariantCulture) +
                    ";HANDLER=" + handlerStart.ToString(CultureInfo.InvariantCulture) + ".." + handlerEnd.ToString(CultureInfo.InvariantCulture) +
                    ";FILTER=" + filter + ";CATCH=" + catchType);
            }

            return true;
        }

        private static bool TryCanonicalOperand(
            DecodedInstruction instruction,
            Dictionary<int, int> offsetToIndex,
            out string canonical,
            out string reason)
        {
            canonical = null;
            reason = null;
            object operand = instruction.Operand;

            switch (instruction.OpCode.OperandType)
            {
                case OperandType.InlineNone:
                    canonical = "NONE";
                    return true;

                case OperandType.ShortInlineI:
                    if (!(operand is sbyte shortInt))
                    {
                        reason = "ShortInlineI operand type mismatch.";
                        return false;
                    }
                    canonical = "I=" + shortInt.ToString(CultureInfo.InvariantCulture);
                    return true;

                case OperandType.InlineI:
                    if (!(operand is int inlineInt))
                    {
                        reason = "InlineI operand type mismatch.";
                        return false;
                    }
                    canonical = "I=" + inlineInt.ToString(CultureInfo.InvariantCulture);
                    return true;

                case OperandType.InlineI8:
                    if (!(operand is long inlineLong))
                    {
                        reason = "InlineI8 operand type mismatch.";
                        return false;
                    }
                    canonical = "I=" + inlineLong.ToString(CultureInfo.InvariantCulture);
                    return true;

                case OperandType.ShortInlineR:
                    if (!(operand is float shortReal))
                    {
                        reason = "ShortInlineR operand type mismatch.";
                        return false;
                    }
                    canonical = "R4=" + BitConverter.ToUInt32(BitConverter.GetBytes(shortReal), 0)
                        .ToString("X8", CultureInfo.InvariantCulture);
                    return true;

                case OperandType.InlineR:
                    if (!(operand is double inlineReal))
                    {
                        reason = "InlineR operand type mismatch.";
                        return false;
                    }
                    canonical = "R8=" + BitConverter.ToUInt64(BitConverter.GetBytes(inlineReal), 0)
                        .ToString("X16", CultureInfo.InvariantCulture);
                    return true;

                case OperandType.InlineVar:
                case OperandType.ShortInlineVar:
                    int variable;
                    if (operand is byte byteVariable)
                        variable = byteVariable;
                    else if (operand is ushort ushortVariable)
                        variable = ushortVariable;
                    else
                    {
                        reason = "variable operand type mismatch.";
                        return false;
                    }

                    string opcodeName = instruction.OpCode.Name ?? string.Empty;
                    string variableKind = opcodeName.IndexOf("arg", StringComparison.Ordinal) >= 0
                        ? "ARG"
                        : opcodeName.IndexOf("loc", StringComparison.Ordinal) >= 0 ? "LOC" : "VAR";
                    canonical = variableKind + "=" + variable.ToString(CultureInfo.InvariantCulture);
                    return true;

                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineBrTarget:
                    if (!(operand is int branchOffset))
                    {
                        reason = "branch operand type mismatch.";
                        return false;
                    }
                    if (!offsetToIndex.TryGetValue(branchOffset, out int branchIndex))
                    {
                        reason = "branch target is not an instruction boundary.";
                        return false;
                    }
                    canonical = "BR=" + branchIndex.ToString(CultureInfo.InvariantCulture);
                    return true;

                case OperandType.InlineSwitch:
                    if (!(operand is int[] switchOffsets))
                    {
                        reason = "switch operand type mismatch.";
                        return false;
                    }
                    var switchIndices = new string[switchOffsets.Length];
                    for (int i = 0; i < switchOffsets.Length; i++)
                    {
                        if (!offsetToIndex.TryGetValue(switchOffsets[i], out int switchIndex))
                        {
                            reason = "switch target is not an instruction boundary.";
                            return false;
                        }
                        switchIndices[i] = switchIndex.ToString(CultureInfo.InvariantCulture);
                    }
                    canonical = "SW=[" + string.Join(",", switchIndices) + "]";
                    return true;

                case OperandType.InlineString:
                    if (!(operand is string text))
                    {
                        reason = "string token did not resolve to a string.";
                        return false;
                    }
                    canonical = "STR=" + Utf8Hex(text);
                    return true;

                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    canonical = "META=" + CanonicalMemberIdentity(operand);
                    return true;

                case OperandType.InlineSig:
                    reason = "InlineSig is forbidden by CANONICAL_IL_V1.";
                    return false;

                default:
                    reason = "unsupported operand type " + instruction.OpCode.OperandType + ".";
                    return false;
            }
        }

        private static string CanonicalMemberIdentity(object member)
        {
            if (member is Type type)
                return CanonicalTypeIdentity(type);
            if (member is FieldInfo field)
                return CanonicalFieldIdentity(field);
            if (member is MethodBase method)
                return CanonicalMethodIdentity(method);
            if (member is MemberInfo memberInfo)
                throw new InvalidDataException("Unsupported resolved member kind: " + memberInfo.MemberType);

            throw new InvalidDataException("Metadata operand did not resolve to a supported semantic member.");
        }

        private static string CanonicalFieldIdentity(FieldInfo field)
        {
            Type declaringType = field.DeclaringType;
            if (declaringType == null)
                throw new InvalidDataException("Field has no declaring type.");

            FieldInfo definition = field;
            if (declaringType.IsGenericType && !declaringType.IsGenericTypeDefinition)
            {
                definition = declaringType.GetGenericTypeDefinition()
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .FirstOrDefault(candidate => candidate.MetadataToken == field.MetadataToken);
                if (definition == null)
                    throw new InvalidDataException("Could not recover generic field definition by metadata token.");
            }

            return "F(D=" + CanonicalTypeIdentity(declaringType) +
                   ";N=" + Utf8Hex(field.Name) +
                   ";T=" + CanonicalTypeIdentity(definition.FieldType) + ")";
        }

        private static string CanonicalMethodIdentity(MethodBase method)
        {
            if (method is MethodInfo constructedMethod && constructedMethod.IsGenericMethod && !constructedMethod.IsGenericMethodDefinition)
            {
                return "MS(B=" + CanonicalMethodIdentity(constructedMethod.GetGenericMethodDefinition()) +
                       ";A=[" + string.Join(",", constructedMethod.GetGenericArguments().Select(CanonicalTypeIdentity).ToArray()) + "])";
            }

            Type declaringType = method.DeclaringType;
            if (declaringType == null)
                throw new InvalidDataException("Method has no declaring type.");

            MethodBase definition = method;
            if (declaringType.IsGenericType && !declaringType.IsGenericTypeDefinition)
            {
                Type genericDefinition = declaringType.GetGenericTypeDefinition();
                definition = genericDefinition
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Cast<MethodBase>()
                    .Concat(genericDefinition
                        .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Cast<MethodBase>())
                    .FirstOrDefault(candidate => candidate.MetadataToken == method.MetadataToken);
                if (definition == null)
                    throw new InvalidDataException("Could not recover generic method definition by metadata token.");
            }

            MethodInfo definitionInfo = definition as MethodInfo;
            Type returnType = definitionInfo != null ? definitionInfo.ReturnType : typeof(void);
            int genericArgumentCount = definition.IsGenericMethod ? definition.GetGenericArguments().Length : 0;
            ParameterInfo[] parameters = definition.GetParameters();

            return "M(D=" + CanonicalTypeIdentity(declaringType) +
                   ";N=" + Utf8Hex(method.Name) +
                   ";S=" + (method.IsStatic ? "1" : "0") +
                   ";GA=" + genericArgumentCount.ToString(CultureInfo.InvariantCulture) +
                   ";R=" + CanonicalTypeIdentity(returnType) +
                   ";P=[" + string.Join(",", parameters.Select(p => CanonicalTypeIdentity(p.ParameterType)).ToArray()) + "])";
        }

        private static string CanonicalTypeIdentity(Type type)
        {
            if (type == null)
                throw new InvalidDataException("Null type cannot be canonicalized.");

            if (type.IsByRef)
                return "BR(" + CanonicalTypeIdentity(type.GetElementType()) + ")";
            if (type.IsPointer)
                return "PTR(" + CanonicalTypeIdentity(type.GetElementType()) + ")";

            if (type.IsArray)
            {
                Type elementType = type.GetElementType();
                if (type.GetArrayRank() == 1 && type == elementType.MakeArrayType())
                    return "SZ(" + CanonicalTypeIdentity(elementType) + ")";

                return "AR(R=" + type.GetArrayRank().ToString(CultureInfo.InvariantCulture) +
                       ";T=" + CanonicalTypeIdentity(elementType) + ";S=;L=)";
            }

            if (type.IsGenericParameter)
            {
                string owner = type.DeclaringMethod != null ? "M" : "T";
                return "GP(" + owner + ";P=" + type.GenericParameterPosition.ToString(CultureInfo.InvariantCulture) + ")";
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                Type definition = type.GetGenericTypeDefinition();
                return "GI(D=" + CanonicalNamedType(definition) +
                       ";A=[" + string.Join(",", type.GetGenericArguments().Select(CanonicalTypeIdentity).ToArray()) + "])";
            }

            return CanonicalNamedType(type);
        }

        private static string CanonicalNamedType(Type type)
        {
            string fullName = type.FullName;
            if (string.IsNullOrEmpty(fullName))
                throw new InvalidDataException("Named type has no FullName: " + type);

            string assemblyName = type.Assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName))
                throw new InvalidDataException("Named type has no assembly simple name: " + fullName);

            if (assemblyName == "mscorlib" || assemblyName == "System" || assemblyName == "System.Core" || assemblyName == "netstandard")
                assemblyName = "CORE";

            return "N(A=" + Utf8Hex(assemblyName) + ";F=" + Utf8Hex(fullName) + ")";
        }

        private static string Utf8Hex(string value)
        {
            return ToHex(Encoding.UTF8.GetBytes(value));
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }
    }
}
