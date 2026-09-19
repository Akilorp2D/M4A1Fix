using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using RoR2;

namespace M4A1Fix.Compatibility
{
    internal static class M4A1Compatibility
    {
        internal const string ExpectedM4A1AssemblySha256 = "600188D306782F6C1B26EA1BDA4A615BFF9AC4437318873D5728CD58B26A7154";
        private const string ExpectedM4A1OnBodyDeathIlSha256 = "79150F880D5A6361BEF0E94A78AABB57B7A56881565C843E8A3D2614D65FE5E4";
        private const int ExpectedTryReviveCodeSize = 632;
        private const int ExpectedRespawnExtraLifeShrineCodeSize = 264;
        private const int ExpectedPendingStateCodeSize = 75;
        private const string ExpectedTryReviveCanonicalSha256 = "43D299DDC88201E148A1D914092175CB58285EDB1683822D825FDCC8E4163395";
        private const string ExpectedRespawnExtraLifeShrineCanonicalSha256 = "28D947645DE9C1E53D7C25591434D8B75ECB2E2E42492DF482D9A57296428E1F";
        private const string ExpectedPendingStateCanonicalSha256 = "7DDA7E015613A20CBD68394435B95ECE885557F7875832D063F853B764157F74";

        internal const string M4A1DummyLinkHooksTypeName = "M4A1Mod.Survivors.M4A1.Hooks.DummyLink";
        internal const string M4A1UtilsTypeName = "M4A1Mod.Survivors.M4A1.Utils.M4A1";

        private static readonly Dictionary<ushort, OpCode> OpCodeMap = BuildOpCodeMap();

        internal static bool TryValidateRevivePriorityTarget(out MethodInfo target, out string reason)
        {
            target = null;
            reason = null;

            Assembly m4a1Assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "M4A1Mod", StringComparison.Ordinal));
            if (m4a1Assembly == null)
            {
                reason = "M4A1Mod assembly is not loaded.";
                return false;
            }

            if (!ValidateAssemblyHash(m4a1Assembly, ExpectedM4A1AssemblySha256, "M4A1Mod.dll", out reason))
                return false;

            if (typeof(CharacterMaster).Assembly != typeof(CharacterBody).Assembly)
            {
                reason = "RoR2 CharacterMaster and CharacterBody are not from the same assembly.";
                return false;
            }

            Type dummyLinkType = m4a1Assembly.GetType(M4A1DummyLinkHooksTypeName, false);
            if (dummyLinkType == null)
            {
                reason = "M4A1 DummyLink hook type is missing.";
                return false;
            }

            target = dummyLinkType.GetMethod(
                "CharacterMaster_OnBodyDeath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (!ValidateM4A1TargetShape(target, out reason))
            {
                target = null;
                return false;
            }

            if (!ValidateMethodIlHash(
                    target,
                    ExpectedM4A1OnBodyDeathIlSha256,
                    "M4A1 CharacterMaster_OnBodyDeath",
                    out reason))
            {
                target = null;
                return false;
            }

            MethodInfo tryRevive = typeof(CharacterMaster).GetMethod(
                "TryReviveOnBodyDeath",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(CharacterBody) },
                null);
            if (tryRevive == null || tryRevive.ReturnType != typeof(bool) || !ValidateTryReviveStructure(tryRevive, out reason))
            {
                target = null;
                if (reason == null)
                    reason = "RoR2 TryReviveOnBodyDeath signature mismatch.";
                return false;
            }

            MethodInfo shrineRespawn = typeof(CharacterMaster).GetMethod(
                "RespawnExtraLifeShrine",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (shrineRespawn == null || shrineRespawn.ReturnType != typeof(void) || !ValidateShrineRespawnStructure(shrineRespawn, out reason))
            {
                target = null;
                if (reason == null)
                    reason = "RoR2 RespawnExtraLifeShrine signature mismatch.";
                return false;
            }

            MethodInfo pendingState = typeof(CharacterMaster).GetMethod(
                "IsExtraLifePendingServer",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (pendingState == null || pendingState.ReturnType != typeof(bool) || !ValidatePendingStateStructure(pendingState, out reason))
            {
                target = null;
                if (reason == null)
                    reason = "RoR2 IsExtraLifePendingServer signature mismatch.";
                return false;
            }

            return true;
        }

        private static bool ValidateM4A1TargetShape(MethodInfo method, out string reason)
        {
            reason = null;
            if (method == null || !method.IsStatic || method.ReturnType != typeof(void))
            {
                reason = "M4A1 CharacterMaster_OnBodyDeath method shape is missing or incompatible.";
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 3 ||
                parameters[0].ParameterType.Name != "orig_OnBodyDeath" ||
                parameters[1].ParameterType != typeof(CharacterMaster) ||
                parameters[2].ParameterType != typeof(CharacterBody))
            {
                reason = "M4A1 CharacterMaster_OnBodyDeath parameter shape is incompatible.";
                return false;
            }

            return true;
        }

        private static bool ValidateAssemblyHash(Assembly assembly, string expected, string displayName, out string reason)
        {
            reason = null;
            string location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location))
            {
                reason = displayName + " location is unavailable for compatibility hashing.";
                return false;
            }

            string actual;
            try
            {
                using (FileStream stream = File.OpenRead(location))
                using (SHA256 sha = SHA256.Create())
                    actual = ToHex(sha.ComputeHash(stream));
            }
            catch (IOException ex)
            {
                reason = displayName + " hash read failed: " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                reason = displayName + " hash read denied: " + ex.Message;
                return false;
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                reason = displayName + " SHA-256 mismatch. Expected " + expected + ", got " + actual + ".";
                return false;
            }

            return true;
        }

        private static bool ValidateTryReviveStructure(MethodInfo method, out string reason)
        {
            if (!ValidateCanonicalFingerprint(
                    method, ExpectedTryReviveCodeSize, ExpectedTryReviveCanonicalSha256,
                    "RoR2 TryReviveOnBodyDeath", out reason))
                return false;

            if (!TryDecode(method, ExpectedTryReviveCodeSize, "RoR2 TryReviveOnBodyDeath", out var instructions, out reason))
                return false;

            int shrineBuff = FindMember(instructions, "DLC2Content+Buffs::ExtraLifeBuff", 0);
            int hasBuff = FindMember(instructions, "CharacterBody::HasBuff", shrineBuff + 1);
            int removeBuff = FindMember(instructions, "CharacterBody::RemoveBuff", hasBuff + 1);
            int shrineString = FindString(instructions, "RespawnExtraLifeShrine", removeBuff + 1);
            int shrineInvoke = FindMember(instructions, "UnityEngine.MonoBehaviour::Invoke(System.String,System.Single)", shrineString + 1);
            int sfxString = FindString(instructions, "PlayExtraLifeSFX", shrineInvoke + 1);
            int sfxInvoke = FindMember(instructions, "UnityEngine.MonoBehaviour::Invoke(System.String,System.Single)", sfxString + 1);
            int dioTake = FindMember(instructions, "ItemTransformation::TryTake", 0);

            if (shrineBuff < 0 ||
                hasBuff <= shrineBuff ||
                removeBuff <= hasBuff ||
                shrineString <= removeBuff ||
                shrineInvoke <= shrineString ||
                sfxString <= shrineInvoke ||
                sfxInvoke <= sfxString ||
                (dioTake >= 0 && dioTake <= sfxInvoke))
            {
                reason = "RoR2 TryReviveOnBodyDeath normalized structural fingerprint mismatch in Shrine-before-Dio decision landmarks.";
                return false;
            }

            return true;
        }

        private static bool ValidateShrineRespawnStructure(MethodInfo method, out string reason)
        {
            if (!ValidateCanonicalFingerprint(
                    method, ExpectedRespawnExtraLifeShrineCodeSize, ExpectedRespawnExtraLifeShrineCanonicalSha256,
                    "RoR2 RespawnExtraLifeShrine", out reason))
                return false;

            if (!TryDecode(method, ExpectedRespawnExtraLifeShrineCodeSize, "RoR2 RespawnExtraLifeShrine", out var instructions, out reason))
                return false;

            int respawn = FindMember(instructions, "CharacterMaster::Respawn", 0);
            int immune = FindMember(instructions, "RoR2Content+Buffs::Immune", 0);
            int addTimedBuff = FindMember(instructions, "CharacterBody::AddTimedBuff", immune < 0 ? 0 : immune);

            if (respawn < 0 || immune < 0 || addTimedBuff < immune)
            {
                reason = "RoR2 RespawnExtraLifeShrine normalized structural fingerprint mismatch in respawn/immunity landmarks.";
                return false;
            }

            return true;
        }

        private static bool ValidatePendingStateStructure(MethodInfo method, out string reason)
        {
            if (!ValidateCanonicalFingerprint(
                    method, ExpectedPendingStateCodeSize, ExpectedPendingStateCanonicalSha256,
                    "RoR2 IsExtraLifePendingServer", out reason))
                return false;

            if (!TryDecode(method, ExpectedPendingStateCodeSize, "RoR2 IsExtraLifePendingServer", out var instructions, out reason))
                return false;

            int behavior = FindMember(instructions, "ExtraLifeServerBehavior", 0);
            int seeker = FindString(instructions, "RespawnSeekerRevive", 0);
            int shrine = FindString(instructions, "RespawnExtraLifeShrine", seeker + 1);
            int heal = FindString(instructions, "RespawnExtraLifeHealAndRevive", shrine + 1);

            if (behavior < 0 || seeker < 0 || shrine <= seeker || heal <= shrine)
            {
                reason = "RoR2 IsExtraLifePendingServer normalized structural fingerprint mismatch in pending-revive landmarks.";
                return false;
            }

            return true;
        }

        private static bool ValidateCanonicalFingerprint(
            MethodInfo method,
            int expectedCodeSize,
            string expectedHash,
            string displayName,
            out string reason)
        {
            reason = null;
            if (!TryBuildCanonicalMethod(method, expectedCodeSize, displayName, out string canonical, out reason))
                return false;

            string actual;
            using (SHA256 sha = SHA256.Create())
                actual = ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)));

            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                reason = displayName + " canonical IL SHA-256 mismatch. Expected " + expectedHash + ", got " + actual + ".";
                return false;
            }

            return true;
        }

        private static bool TryBuildCanonicalMethod(
            MethodInfo method,
            int expectedCodeSize,
            string displayName,
            out string canonical,
            out string reason)
        {
            canonical = null;
            reason = null;

            if (!TryDecode(method, expectedCodeSize, displayName, out var instructions, out reason))
                return false;

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

                IList<LocalVariableInfo> locals = body.LocalVariables;
                lines.Add("LOCALS=" + locals.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < locals.Count; i++)
                {
                    LocalVariableInfo local = locals[i];
                    lines.Add(
                        "LOCAL[" + i.ToString(CultureInfo.InvariantCulture) + "]=T=" +
                        CanonicalTypeIdentity(local.LocalType) + ";PINNED=" + (local.IsPinned ? "1" : "0"));
                }

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

                canonical = string.Join("\n", lines.ToArray()) + "\n";
                return true;
            }
            catch (Exception ex)
            {
                reason = displayName + " canonicalization failed closed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
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

        private static bool ValidateMethodIlHash(MethodInfo method, string expected, string displayName, out string reason)
        {
            reason = null;
            MethodBody body = method.GetMethodBody();
            byte[] bytes = body != null ? body.GetILAsByteArray() : null;
            if (bytes == null)
            {
                reason = displayName + " has no inspectable IL body.";
                return false;
            }

            string actual;
            using (SHA256 sha = SHA256.Create())
                actual = ToHex(sha.ComputeHash(bytes));

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                reason = displayName + " IL SHA-256 mismatch. Expected " + expected + ", got " + actual + ".";
                return false;
            }

            return true;
        }

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

        private static bool TryDecode(
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

        private static int FindMember(List<DecodedInstruction> instructions, string contains, int start)
        {
            for (int i = Math.Max(start, 0); i < instructions.Count; i++)
            {
                string id = MemberId(instructions[i].Operand);
                if (id != null && id.Contains(contains))
                    return i;
            }
            return -1;
        }

        private static int FindString(List<DecodedInstruction> instructions, string expected, int start)
        {
            for (int i = Math.Max(start, 0); i < instructions.Count; i++)
            {
                if (string.Equals(instructions[i].Operand as string, expected, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        private sealed class DecodedInstruction
        {
            internal int Offset;
            internal OpCode OpCode;
            internal object Operand;
        }
    }
}
