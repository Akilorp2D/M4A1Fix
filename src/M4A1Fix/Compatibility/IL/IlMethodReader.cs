using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace M4A1Fix.Compatibility.IL
{
    // Reads original method bodies; it does not install hooks or execute game code.
    internal static class IlMethodReader
    {
        private static readonly Dictionary<ushort, OpCode> OpCodeMap = BuildOpCodeMap();

        private static Dictionary<ushort, OpCode> BuildOpCodeMap()
        {
            var map = new Dictionary<ushort, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(OpCode))
                    continue;
                OpCode opcode = (OpCode)field.GetValue(null);
                map[(ushort)opcode.Value] = opcode;
            }
            return map;
        }

        internal static bool TryRead(
            MethodInfo method,
            int expectedCodeSize,
            string displayName,
            out List<DecodedInstruction> instructions,
            out string reason)
        {
            instructions = null;
            reason = null;
            MethodBody body = method.GetMethodBody();
            byte[] il = body != null ? body.GetILAsByteArray() : null;
            if (il == null)
            {
                reason = displayName + " has no inspectable IL body.";
                return false;
            }

            if (il.Length != expectedCodeSize)
            {
                reason = displayName + " IL code-size mismatch. Expected " + expectedCodeSize.ToString(CultureInfo.InvariantCulture) +
                         ", got " + il.Length.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            var decoded = new List<DecodedInstruction>();
            int position = 0;

            try
            {
                while (position < il.Length)
                {
                    int offset = position;
                    ushort value = il[position++];
                    if (value == 0xFE)
                    {
                        if (position >= il.Length)
                            throw new InvalidDataException("truncated two-byte opcode");
                        value = (ushort)(0xFE00 | il[position++]);
                    }

                    if (!OpCodeMap.TryGetValue(value, out OpCode opcode))
                        throw new InvalidDataException("unknown opcode 0x" + value.ToString("X4"));

                    object operand = ReadOperand(method, il, opcode, ref position);

                    decoded.Add(new DecodedInstruction { Offset = offset, OpCode = opcode, Operand = operand });
                }
            }
            catch (Exception ex)
            {
                reason = displayName + " normalized IL inspection failed closed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            var offsets = new HashSet<int>(decoded.Select(instruction => instruction.Offset));
            offsets.Add(il.Length);
            foreach (DecodedInstruction instruction in decoded)
            {
                if (instruction.Operand is int branchTarget &&
                    (instruction.OpCode.OperandType == OperandType.InlineBrTarget ||
                     instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget) &&
                    !offsets.Contains(branchTarget))
                {
                    reason = displayName + " has a branch target outside instruction boundaries.";
                    return false;
                }

                if (instruction.Operand is int[] switchTargets && switchTargets.Any(target => !offsets.Contains(target)))
                {
                    reason = displayName + " has an invalid switch target.";
                    return false;
                }
            }

            instructions = decoded;
            return true;
        }

        private static object ReadOperand(MethodInfo method, byte[] il, OpCode opcode, ref int position)
        {
            object operand = null;
            switch (opcode.OperandType)
            {
                case OperandType.ShortInlineI:
                    operand = (sbyte)il[position++];
                    break;
                case OperandType.InlineI:
                    operand = BitConverter.ToInt32(il, position);
                    position += 4;
                    break;
                case OperandType.InlineI8:
                    operand = BitConverter.ToInt64(il, position);
                    position += 8;
                    break;
                case OperandType.ShortInlineR:
                    operand = BitConverter.ToSingle(il, position);
                    position += 4;
                    break;
                case OperandType.InlineR:
                    operand = BitConverter.ToDouble(il, position);
                    position += 8;
                    break;
                case OperandType.ShortInlineVar:
                    operand = il[position++];
                    break;
                case OperandType.InlineVar:
                    operand = BitConverter.ToUInt16(il, position);
                    position += 2;
                    break;
                case OperandType.ShortInlineBrTarget:
                    operand = position + 1 + (sbyte)il[position];
                    position += 1;
                    break;
                case OperandType.InlineBrTarget:
                    int relative = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = position + relative;
                    break;
                case OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, position);
                    position += 4;
                    int switchBase = position + (4 * count);
                    var targets = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        targets[i] = switchBase + BitConverter.ToInt32(il, position);
                        position += 4;
                    }
                    operand = targets;
                    break;
                case OperandType.InlineString:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = method.Module.ResolveString(token);
                    if (operand == null)
                        throw new InvalidDataException("unresolved string token");
                    break;
                }
                case OperandType.InlineField:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = method.Module.ResolveField(token, method.DeclaringType.GetGenericArguments(), method.GetGenericArguments());
                    if (operand == null)
                        throw new InvalidDataException("unresolved field token");
                    break;
                }
                case OperandType.InlineMethod:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = method.Module.ResolveMethod(token, method.DeclaringType.GetGenericArguments(), method.GetGenericArguments());
                    if (operand == null)
                        throw new InvalidDataException("unresolved method token");
                    break;
                }
                case OperandType.InlineType:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = method.Module.ResolveType(token, method.DeclaringType.GetGenericArguments(), method.GetGenericArguments());
                    if (operand == null)
                        throw new InvalidDataException("unresolved type token");
                    break;
                }
                case OperandType.InlineTok:
                {
                    int token = BitConverter.ToInt32(il, position);
                    position += 4;
                    operand = method.Module.ResolveMember(token, method.DeclaringType.GetGenericArguments(), method.GetGenericArguments());
                    if (operand == null)
                        throw new InvalidDataException("unresolved member token");
                    break;
                }
                case OperandType.InlineSig:
                    throw new InvalidDataException("InlineSig is not accepted by this fail-closed contract");
                case OperandType.InlineNone:
                    break;
                default:
                    throw new InvalidDataException("unsupported operand type " + opcode.OperandType);
            }
            return operand;
        }

        private static string MemberId(object operand)
        {
            MemberInfo member = operand as MemberInfo;
            if (member == null)
                return null;

            if (member is MethodBase method)
            {
                string parameters = string.Join(",", method.GetParameters()
                    .Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name)
                    .ToArray());
                MethodInfo methodInfo = method as MethodInfo;
                string returnType = methodInfo != null
                    ? methodInfo.ReturnType.FullName ?? methodInfo.ReturnType.Name
                    : "void";
                return (member.DeclaringType != null ? member.DeclaringType.FullName : string.Empty) +
                       "::" + member.Name + "(" + parameters + ")->" + returnType;
            }

            if (member is FieldInfo field)
            {
                return (field.DeclaringType != null ? field.DeclaringType.FullName : string.Empty) +
                       "::" + field.Name + ":" + (field.FieldType.FullName ?? field.FieldType.Name);
            }

            if (member is Type type)
                return type.FullName;

            return (member.DeclaringType != null ? member.DeclaringType.FullName : string.Empty) + "::" + member.Name;
        }

        internal static int FindMember(List<DecodedInstruction> instructions, string contains, int start)
        {
            for (int i = Math.Max(start, 0); i < instructions.Count; i++)
            {
                string id = MemberId(instructions[i].Operand);
                if (id != null && id.Contains(contains))
                    return i;
            }
            return -1;
        }

        internal static int FindString(List<DecodedInstruction> instructions, string expected, int start)
        {
            for (int i = Math.Max(start, 0); i < instructions.Count; i++)
            {
                if (string.Equals(instructions[i].Operand as string, expected, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }
    }
}
