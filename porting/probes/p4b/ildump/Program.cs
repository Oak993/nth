// P4b helper: lists the IL of selected types and methods of a .NET assembly, with tokens resolved to names.
// Usage: ildump <assembly.dll> <typeRegex> [methodRegex]
//        ildump <assembly.dll> --types <typeRegex>        (type and member names only)
//        ildump <assembly.dll> --strings <regex>          (methods whose IL loads a matching user string)
// A throwaway reading aid for the probe (the Avalonia NuGet packages ship no source). Never production code.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

static class IlDumpProgram
{
    static readonly OpCode?[] Ops1 = new OpCode?[256];
    static readonly OpCode?[] Ops2 = new OpCode?[256];

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: ildump <dll> <typeRegex> [methodRegex] | --types <rx> | --strings <rx>"); return 2; }
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var o = (OpCode)f.GetValue(null)!;
            ushort v = (ushort)o.Value;
            if (v < 0x100) Ops1[v] = o; else if ((v & 0xFF00) == 0xFE00) Ops2[v & 0xFF] = o;
        }
        using var fs = File.OpenRead(args[0]);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        var names = new Names(md);
        var sb = new StringBuilder();

        if (args[1] == "--types")
        {
            var rx = new Regex(args[2]);
            foreach (var th in md.TypeDefinitions)
            {
                string tn = names.TypeDef(th);
                if (!rx.IsMatch(tn)) continue;
                var td = md.GetTypeDefinition(th);
                sb.AppendLine($"type {tn} : {(td.BaseType.IsNil ? "-" : names.Type(td.BaseType))} [{td.Attributes}]");
                foreach (var fh in td.GetFields())
                {
                    var fd = md.GetFieldDefinition(fh);
                    sb.AppendLine($"  field {fd.DecodeSignature(names.Provider, null)} {md.GetString(fd.Name)} [{fd.Attributes}]");
                }
                foreach (var mh in td.GetMethods())
                    sb.AppendLine($"  method {names.MethodDefSig(mh)}");
            }
            Console.Write(sb.ToString());
            return 0;
        }

        if (args[1] == "--strings")
        {
            var rx = new Regex(args[2]);
            foreach (var th in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(th);
                foreach (var mh in td.GetMethods())
                {
                    var m = md.GetMethodDefinition(mh);
                    if (m.RelativeVirtualAddress == 0) continue;
                    var body = pe.GetMethodBody(m.RelativeVirtualAddress);
                    var il = body.GetILBytes() ?? Array.Empty<byte>();
                    foreach (var (_, text) in Decode(il, names))
                        if (text.StartsWith("ldstr", StringComparison.Ordinal) && rx.IsMatch(text)) { sb.AppendLine($"{names.MethodDefSig(mh)}  {text}"); }
                }
            }
            Console.Write(sb.ToString());
            return 0;
        }

        var typeRx = new Regex(args[1]);
        var methodRx = args.Length > 2 ? new Regex(args[2]) : null;
        foreach (var th in md.TypeDefinitions)
        {
            string tn = names.TypeDef(th);
            if (!typeRx.IsMatch(tn)) continue;
            var td = md.GetTypeDefinition(th);
            bool headerDone = false;
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                string mn = md.GetString(m.Name);
                if (methodRx != null && !methodRx.IsMatch(mn)) continue;
                if (!headerDone)
                {
                    sb.AppendLine($"// ===== type {tn} : {(td.BaseType.IsNil ? "-" : names.Type(td.BaseType))}");
                    foreach (var fh in td.GetFields())
                    {
                        var fd = md.GetFieldDefinition(fh);
                        sb.AppendLine($"//   field {fd.DecodeSignature(names.Provider, null)} {md.GetString(fd.Name)}");
                    }
                    headerDone = true;
                }
                sb.AppendLine($"method {names.MethodDefSig(mh)} [{m.Attributes}] rva=0x{m.RelativeVirtualAddress:X}");
                if (m.RelativeVirtualAddress == 0) { sb.AppendLine("  (no body)"); continue; }
                var body = pe.GetMethodBody(m.RelativeVirtualAddress);
                if (!body.LocalSignature.IsNil)
                {
                    var ss = md.GetStandaloneSignature(body.LocalSignature);
                    var locals = ss.DecodeLocalSignature(names.Provider, null);
                    for (int i = 0; i < locals.Length; i++) sb.AppendLine($"  .local V_{i} {locals[i]}");
                }
                foreach (var r in body.ExceptionRegions)
                    sb.AppendLine($"  .{r.Kind} try IL_{r.TryOffset:X4}..IL_{r.TryOffset + r.TryLength:X4} handler IL_{r.HandlerOffset:X4}..IL_{r.HandlerOffset + r.HandlerLength:X4}" +
                                  (r.Kind == ExceptionRegionKind.Catch ? $" catch {names.Type(r.CatchType)}" : ""));
                var il = body.GetILBytes() ?? Array.Empty<byte>();
                foreach (var (off, text) in Decode(il, names)) sb.AppendLine($"  IL_{off:X4}: {text}");
                sb.AppendLine();
            }
        }
        Console.Write(sb.ToString());
        return 0;
    }

    static IEnumerable<(int off, string text)> Decode(byte[] il, Names names)
    {
        int p = 0;
        while (p < il.Length)
        {
            int start = p;
            OpCode? op;
            byte b = il[p++];
            if (b == 0xFE) { op = Ops2[il[p++]]; }
            else op = Ops1[b];
            if (op is null) { yield return (start, $".byte 0x{b:X2}"); continue; }
            var o = op.Value;
            string operand = "";
            switch (o.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: { sbyte d = (sbyte)il[p]; p += 1; operand = $"IL_{p + d:X4}"; break; }
                case OperandType.InlineBrTarget: { int d = BitConverter.ToInt32(il, p); p += 4; operand = $"IL_{p + d:X4}"; break; }
                case OperandType.ShortInlineI: { operand = (o.Value == OpCodes.Ldc_I4_S.Value ? (int)(sbyte)il[p] : (int)il[p]).ToString(); p += 1; break; }
                case OperandType.ShortInlineVar: { operand = il[p].ToString(); p += 1; break; }
                case OperandType.InlineVar: { operand = BitConverter.ToUInt16(il, p).ToString(); p += 2; break; }
                case OperandType.InlineI: { int v = BitConverter.ToInt32(il, p); p += 4; operand = $"{v} (0x{v:X})"; break; }
                case OperandType.InlineI8: { operand = BitConverter.ToInt64(il, p).ToString(); p += 8; break; }
                case OperandType.ShortInlineR: { operand = BitConverter.ToSingle(il, p).ToString(System.Globalization.CultureInfo.InvariantCulture); p += 4; break; }
                case OperandType.InlineR: { operand = BitConverter.ToDouble(il, p).ToString(System.Globalization.CultureInfo.InvariantCulture); p += 8; break; }
                case OperandType.InlineSwitch:
                    {
                        int n = BitConverter.ToInt32(il, p); p += 4;
                        int baseOff = p + 4 * n;
                        var t = new List<string>();
                        for (int i = 0; i < n; i++) { int d = BitConverter.ToInt32(il, p); p += 4; t.Add($"{i}:IL_{baseOff + d:X4}"); }
                        operand = "(" + string.Join(", ", t) + ")";
                        break;
                    }
                case OperandType.InlineString:
                    {
                        int tok = BitConverter.ToInt32(il, p); p += 4;
                        operand = "\"" + names.Md.GetUserString(MetadataTokens.UserStringHandle(tok & 0xFFFFFF)).Replace("\n", "\\n") + "\"";
                        break;
                    }
                default:
                    {
                        int tok = BitConverter.ToInt32(il, p); p += 4;
                        operand = names.Token(tok);
                        break;
                    }
            }
            yield return (start, operand.Length > 0 ? $"{o.Name} {operand}" : o.Name ?? "?");
        }
    }
}

sealed class Names
{
    public readonly MetadataReader Md;
    public readonly StrProvider Provider;
    public Names(MetadataReader md) { Md = md; Provider = new StrProvider(this); }

    public string TypeDef(TypeDefinitionHandle h)
    {
        var td = Md.GetTypeDefinition(h);
        string n = Md.GetString(td.Name);
        var decl = td.GetDeclaringType();
        if (!decl.IsNil) return TypeDef(decl) + "/" + n;
        string ns = Md.GetString(td.Namespace);
        return ns.Length > 0 ? ns + "." + n : n;
    }

    public string TypeRef(TypeReferenceHandle h)
    {
        var tr = Md.GetTypeReference(h);
        string n = Md.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference) return TypeRef((TypeReferenceHandle)tr.ResolutionScope) + "/" + n;
        string ns = Md.GetString(tr.Namespace);
        return ns.Length > 0 ? ns + "." + n : n;
    }

    public string Type(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => TypeDef((TypeDefinitionHandle)h),
        HandleKind.TypeReference => TypeRef((TypeReferenceHandle)h),
        HandleKind.TypeSpecification => Md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(Provider, null),
        _ => h.Kind.ToString(),
    };

    public string MethodDefSig(MethodDefinitionHandle h)
    {
        var m = Md.GetMethodDefinition(h);
        var sig = m.DecodeSignature(Provider, null);
        var pnames = new Dictionary<int, string>();
        foreach (var ph in m.GetParameters()) { var pp = Md.GetParameter(ph); pnames[pp.SequenceNumber] = Md.GetString(pp.Name); }
        var ps = sig.ParameterTypes.Select((t, i) => pnames.TryGetValue(i + 1, out var pn) ? $"{t} {pn}" : t);
        return $"{sig.ReturnType} {TypeDef(m.GetDeclaringType())}::{Md.GetString(m.Name)}({string.Join(", ", ps)})";
    }

    public string Token(int tok)
    {
        var h = MetadataTokens.EntityHandle(tok);
        switch (h.Kind)
        {
            case HandleKind.MethodDefinition:
                {
                    var m = Md.GetMethodDefinition((MethodDefinitionHandle)h);
                    var sig = m.DecodeSignature(Provider, null);
                    return $"{sig.ReturnType} {TypeDef(m.GetDeclaringType())}::{Md.GetString(m.Name)}({string.Join(", ", sig.ParameterTypes)})";
                }
            case HandleKind.MemberReference:
                {
                    var mr = Md.GetMemberReference((MemberReferenceHandle)h);
                    string parent = mr.Parent.Kind switch
                    {
                        HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification => Type(mr.Parent),
                        HandleKind.MethodDefinition => Token(MetadataTokens.GetToken(mr.Parent)),
                        _ => mr.Parent.Kind.ToString(),
                    };
                    string name = Md.GetString(mr.Name);
                    if (mr.GetKind() == MemberReferenceKind.Method)
                    {
                        var sig = mr.DecodeMethodSignature(Provider, null);
                        return $"{sig.ReturnType} {parent}::{name}({string.Join(", ", sig.ParameterTypes)})";
                    }
                    return $"{mr.DecodeFieldSignature(Provider, null)} {parent}::{name}";
                }
            case HandleKind.MethodSpecification:
                {
                    var ms = Md.GetMethodSpecification((MethodSpecificationHandle)h);
                    var args = ms.DecodeSignature(Provider, null);
                    return Token(MetadataTokens.GetToken(ms.Method)) + "<" + string.Join(", ", args) + ">";
                }
            case HandleKind.FieldDefinition:
                {
                    var fd = Md.GetFieldDefinition((FieldDefinitionHandle)h);
                    return $"{fd.DecodeSignature(Provider, null)} {TypeDef(fd.GetDeclaringType())}::{Md.GetString(fd.Name)}";
                }
            case HandleKind.TypeDefinition:
            case HandleKind.TypeReference:
            case HandleKind.TypeSpecification:
                return Type(h);
            case HandleKind.StandaloneSignature:
                return "sig";
            default:
                return $"tok 0x{tok:X8}";
        }
    }
}

sealed class StrProvider : ISignatureTypeProvider<string, object?>
{
    readonly Names _n;
    public StrProvider(Names n) { _n = n; }
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => $"fnptr {signature.ReturnType}({string.Join(",", signature.ParameterTypes)})";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => _n.TypeDef(handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => _n.TypeRef(handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
