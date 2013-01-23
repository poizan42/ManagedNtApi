using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ICSharpCode.NRefactory.CSharp;
using System.IO;
using ICSharpCode.NRefactory.CSharp.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Semantics;
using System.Linq.Expressions;
using ICSharpCode.NRefactory.TypeSystem.Implementation;
using System.Runtime.InteropServices;
using AstAttribute = ICSharpCode.NRefactory.CSharp.Attribute;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using AstExpression = ICSharpCode.NRefactory.CSharp.Expression;
using Attribute = System.Attribute;

namespace StructFlattener
{
  class Flattener
  {
    private static readonly HashSet<string> specialAttributeNames;

    static Flattener()
    {
      specialAttributeNames = new HashSet<string>(new string[] {
        "FlattenAttribute", "AnonymousStructAttribute", "UnionAttribute"
      });
    }

    private class FlattenedStructAnnotation
    {
      public int Size;
      public int LargestMemberAlignSize;

      public FlattenedStructAnnotation(int size, int largestMemberAlignSize)
      {
        this.Size = size;
        this.LargestMemberAlignSize = largestMemberAlignSize;
      }
    }
    
    private class FlattenedStructMemberAnnotation
    {
      public int Offset;

      public FlattenedStructMemberAnnotation(int offset)
      {
        this.Offset = offset;
      }
    }

    private CSharpParser parser;
    private SyntaxTree root;
    private IProjectContent pc;
    private CSharpUnresolvedFile ts;
    private ICompilation compilation;
    private CSharpAstResolver resolver;
    private Dictionary<IType, TypeDeclaration> flattenedStructCache;
    private string filename;
    private Dictionary<string, SyntaxTree> additionalFiles;
    bool is64Bit;

    public bool InlineNestedStructs { get; set; }

    public Flattener(string filename, bool is64Bit)
    {
      this.is64Bit = is64Bit;
      this.filename = filename;
      this.InlineNestedStructs = true;

      flattenedStructCache = new Dictionary<IType, TypeDeclaration>();
      additionalFiles = new Dictionary<string, SyntaxTree>();

      parser = new CSharpParser();
      parser.CompilerSettings.ConditionalSymbols.Add(is64Bit ? "X64" : "X86");
      TextReader tr = File.OpenText(filename);
      root = parser.Parse(tr, filename);
      pc = new CSharpProjectContent();
      ts = root.ToTypeSystem();
      pc = pc.AddOrUpdateFiles(ts);
      pc = pc.AddAssemblyReferences(
        //mscorlib
        new CecilLoader().LoadAssemblyFile(typeof(int).Assembly.Location));
    }

    public string FlattenStructsInFile()
    {
      compilation = pc.CreateCompilation();
      resolver = new CSharpAstResolver(compilation, root, ts);

      var structs = root.Descendants.OfType<TypeDeclaration>()
        .Where(n => n.TypeKeyword.Role == Roles.StructKeyword);

      foreach (var s in structs)
        FlattenAndReplaceStructIfNeeded(s);

      CleanUpAttributes();

      return root.GetText();
    }

    private void CleanUpAttributes()
    {
      var decls = root.Descendants.OfType<EntityDeclaration>();
      CSharpAstResolver resolver = new CSharpAstResolver(compilation, root);
      IType attributeType = compilation.FindType(typeof(Attribute));
      List<AstNode> toDelete = new List<AstNode>();

      foreach (var decl in decls)
      {
        //Delete dummy attribute declarations
        if (decl is TypeDeclaration && ((TypeDeclaration)decl).TypeKeyword.Role == Roles.ClassKeyword)
        {
          TypeDeclaration c = (TypeDeclaration)decl;
          if (!specialAttributeNames.Contains(c.Name))
            continue;
          foreach (AstType astType in c.BaseTypes)
          {
            IType baseType = resolver.Resolve(astType).Type;
            if (baseType == attributeType)
            {
              toDelete.Add(decl);
              break;
            }
          }
        }
        foreach (var attr in decl.Attributes
          .SelectMany(attrSect => attrSect.Attributes))
        {
          string simpleTypeName = attr.Type is SimpleType ? ((SimpleType)attr.Type).Identifier : null;
          if (simpleTypeName != null && !simpleTypeName.EndsWith("Attribute", StringComparison.Ordinal))
            simpleTypeName += "Attribute";
          //Is it a struct that is itself marked anonymous? Then remove it completely
          if (decl is TypeDeclaration &&
            ((TypeDeclaration)decl).TypeKeyword.Role == Roles.StructKeyword &&
            simpleTypeName == "AnonymousStructAttribute")
          {
            toDelete.Add(decl);
            break; //Not much in removing the attributes then...
          }
          if (specialAttributeNames.Contains(simpleTypeName))
            toDelete.Add(attr);
        }
      }
      foreach (AstNode node in toDelete)
      {
        if (node is AstAttribute)
          RemoveAttribute((AstAttribute)node);
        else
          node.Remove();
      }
    }

    private void FlattenAndReplaceStructIfNeeded(TypeDeclaration s)
    {
      bool shouldFlatten = UnresolvedHasAttribute(s, "FlattenAttribute");
      bool autoUnion = UnresolvedHasAttribute(s, "UnionAttribute");
      if (!shouldFlatten)
        return;

      TypeDeclaration newStruct = FlattenStruct(s, autoUnion);
      s.ReplaceWith(newStruct); 
    }

    private AstAttribute UnresolvedGetAttribute(EntityDeclaration s, string attributeName)
    {
      foreach (var attr in s.Attributes
        .SelectMany(attrSect => attrSect.Attributes))
      {
        string simpleTypeName = attr.Type is SimpleType ? ((SimpleType)attr.Type).Identifier : null;
        if (simpleTypeName != null && !simpleTypeName.EndsWith("Attribute", StringComparison.Ordinal))
          simpleTypeName += "Attribute";
        if (simpleTypeName == attributeName)
          return attr;
      }
      return null;
    }

    private bool UnresolvedHasAttribute(EntityDeclaration s, string attributeName)
    {
      return UnresolvedGetAttribute(s, attributeName) != null;
    }

    private void RemoveAttribute(AstAttribute attr)
    {
      AttributeSection section = attr.Parent as AttributeSection;
      attr.Remove();
      if (section != null && section.Attributes.Count == 0)
        section.Remove();
    }

    private CSharpInvocationResolveResult GetAttributeConstruction(EntityDeclaration s,
      IType attributeType, CSharpAstResolver curResolver = null)
    {
      AstAttribute dummy;
      return GetAttributeConstruction(s, attributeType, out dummy, curResolver);
    }

    private CSharpInvocationResolveResult GetAttributeConstruction(EntityDeclaration s,
      IType attributeType, out AstAttribute attributeAst, 
      CSharpAstResolver curResolver = null)
    {
      curResolver = curResolver ?? resolver;
      foreach (var attr in s.Attributes
        .SelectMany(attrSect => attrSect.Attributes))
      {
        CSharpInvocationResolveResult resolved = curResolver.Resolve(attr) as CSharpInvocationResolveResult;
        if (resolved != null && resolved.Type == attributeType)
        {
          attributeAst = attr;
          return resolved;
        }
      }
      attributeAst = null;
      return null;
    }

    private void ReplaceAttribute(EntityDeclaration target, IAttribute newAttribute, 
      TypeSystemAstBuilder astBuilder, CSharpAstResolver curResolver = null)
    {
      AstAttribute newAstAttribute = astBuilder.ConvertAttribute(newAttribute);
      AstAttribute oldAstAttribute;
      GetAttributeConstruction(target, newAttribute.AttributeType, out oldAstAttribute, curResolver);
      if (oldAstAttribute != null)
        oldAstAttribute.ReplaceWith(newAstAttribute);
      else
      {
        AttributeSection section = new AttributeSection(newAstAttribute);
        target.Attributes.Add(section);
      }
    }
    
    private int GetFieldOffsetAttribute(EntityDeclaration entity, CSharpAstResolver curResolver = null)
    {
      IType fieldOffsetAttributeType = compilation.FindType(typeof(FieldOffsetAttribute));
      var attr = GetAttributeConstruction(entity, fieldOffsetAttributeType, curResolver);
      if (attr == null)
        throw new Exception("Missing FieldOffsetAttribute from member in struct with explicit layout.\n"+
          String.Format("Struct: {0}, field: {1}", GetAstDescription(entity.Parent), GetAstDescription(entity)));
      return (int)attr.GetArgumentsForCall()[0].ConstantValue;
    }

    private string GetAstDescription(AstNode astNode)
    {
      if (astNode is EntityDeclaration)
      {
        if (astNode is FieldDeclaration || astNode is FixedFieldDeclaration)
          return astNode.GetText();
        return ((EntityDeclaration)astNode).Name;
      }
      return astNode.ToString();
    }

    private void SetFieldOffsetAttribute(EntityDeclaration target, int offset,
      TypeSystemAstBuilder astBuilder, CSharpAstResolver curResolver = null)
    {
      IAttribute newAttr = CreateFieldOffsetAttribute(offset);
      ReplaceAttribute(target, newAttr, astBuilder, curResolver);
    }

    private IAttribute CreateFieldOffsetAttribute(int offset)
    {
      IType fieldOffsetAttributeType = compilation.FindType(typeof(FieldOffsetAttribute));
      IType intType = compilation.FindType(KnownTypeCode.Int32);
      var positionalArgs = new ResolveResult[] { new ConstantResolveResult(intType, offset) };
      return new DefaultAttribute(fieldOffsetAttributeType, positionalArgs);
    }
    
    private void UpdateStructLayoutAttribute(TypeDeclaration newStruct, LayoutKind layoutKind, 
      int pack, int size, TypeSystemAstBuilder astBuilder, CSharpAstResolver curResolver = null)
    {
      IAttribute newIAttr = CreateStructLayoutAttribute(LayoutKind.Explicit, pack, size);
      ReplaceAttribute(newStruct, newIAttr, astBuilder, curResolver);
    }

    private IAttribute CreateStructLayoutAttribute(LayoutKind layoutKind, int pack, int size)
    {
      IType layoutAttributeType = compilation.FindType(
        new FullTypeName("System.Runtime.InteropServices.StructLayoutAttribute"));
      IType intType = compilation.FindType(KnownTypeCode.Int32);
      var namedArgumentsDict = new Dictionary<IMember, ResolveResult>();
      IMember packMember = layoutAttributeType.GetMembers(m => m.Name == "Pack").First();
      IMember sizeMember = layoutAttributeType.GetMembers(m => m.Name == "Size").First();
      namedArgumentsDict[packMember] = new ConstantResolveResult(intType, pack);
      namedArgumentsDict[sizeMember] = new ConstantResolveResult(intType, size);
      IType layoutKindType = compilation.FindType(typeof(LayoutKind));
      IMember sequentialLayoutKindMember = layoutKindType
        .GetMembers(m => m.Name == Enum.GetName(typeof(LayoutKind), layoutKind)).First();
      var positionalArguments = new ResolveResult[1];
      positionalArguments[0] = new MemberResolveResult(
        new TypeResolveResult(layoutKindType), sequentialLayoutKindMember);
      var namedArguments = namedArgumentsDict.ToArray();
      return new DefaultAttribute(layoutAttributeType, positionalArguments, namedArguments);
    }

    private StructLayoutAttribute GetStructLayout(TypeDeclaration s, CSharpAstResolver curResolver = null)
    {
      IType structLayoutType = compilation.FindType(typeof(StructLayoutAttribute));
      var layout = GetAttributeConstruction(s, structLayoutType, curResolver);
      int pack = 0;
      int size = 0;
      LayoutKind layoutKind = LayoutKind.Sequential;
      if (layout != null)
      {
        layoutKind = (LayoutKind)(int)layout.GetArgumentsForCall()[0].ConstantValue;
        if (layoutKind == LayoutKind.Auto)
          layoutKind = LayoutKind.Sequential;

        //Get named arguments Pack and Size
        foreach (var arg in layout.InitializerStatements
          .OfType<OperatorResolveResult>()
          .Where(arg => arg.OperatorType == ExpressionType.Assign))
        {
          if (arg.Operands[0] is MemberResolveResult)
          {
            string memberName = ((MemberResolveResult)arg.Operands[0]).Member.Name;
            object value = arg.Operands[1].ConstantValue;
            GetCheckAttrInitializer(memberName, arg.Operands[1], "Pack", layout.Type.Name,
              s.Name, ref value);
            GetCheckAttrInitializer(memberName, arg.Operands[1], "Size", layout.Type.Name,
              s.Name, ref size);
          }
        }
      }
      if (pack == 0)
        pack = 8; //8 is the size of the largest primitive type (double/x64 pointer).

      return new StructLayoutAttribute(layoutKind) { Size = size, Pack = pack };
    }

    private bool GetCheckAttrInitializer<T>(string memberName, ResolveResult value, 
      string expectedMemberName, string invokeableName, string structName, ref T valueOut)
    {
      if (memberName != expectedMemberName)
        return false;
      if (!(value.ConstantValue is T))
      {
        throw new Exception(String.Format(
          "Invalid value for {0} parameter to {1} " + 
          "attribute for {2}, got {3}", memberName, invokeableName, structName, value));
      }
      valueOut = (T)value.ConstantValue;
      return true;
    }

    private int ResolveTypeSize(AstType astType, TypeSystemAstBuilder astBuilder, 
      out TypeDeclaration flattenedStruct, CSharpAstResolver curResolver = null)
    {
      curResolver = curResolver ?? resolver;
      ResolveResult fieldType = curResolver.Resolve(astType);
      if (fieldType.IsError)
        throw new Exception(String.Format("Could not resolve member type {0} (resolved {1}, type {2})", 
          astType, fieldType, fieldType.Type));
      return ResolveTypeSize(fieldType.Type, astBuilder, out flattenedStruct);
    }

    private int ResolveTypeSize(IType type, TypeSystemAstBuilder astBuilder, 
      out TypeDeclaration flattenedStruct)
    {
      flattenedStruct = null;
      switch (type.Kind)
      {
        case TypeKind.Pointer:
          return is64Bit ? 8 : 4;
        case TypeKind.Struct:
          ITypeDefinition typeDef = type.GetDefinition();
          if (typeDef.KnownTypeCode != KnownTypeCode.None)
            return GetKnownTypeSize(typeDef.KnownTypeCode);
          TypeDeclaration structDecl;
          if (!flattenedStructCache.TryGetValue(type, out structDecl))
          {
            SyntaxTree fileRoot;
            structDecl = GetStructDeclaration(type, out fileRoot);
            if (structDecl == null) //external type
              structDecl = (TypeDeclaration)astBuilder.ConvertEntity(typeDef);
            CSharpResolver otherFileResolver = null;
            if (fileRoot != root)
            {
              CSharpAstResolver astResolver = new CSharpAstResolver(compilation, fileRoot);
              otherFileResolver = astResolver.GetResolverStateBefore(structDecl);
            }
            structDecl = FlattenStruct(structDecl, type, otherFileResolver);
          }
          flattenedStruct = structDecl;
          return structDecl.Annotation<FlattenedStructAnnotation>().Size;
        case TypeKind.Enum:
          return ResolveTypeSize(type.GetDefinition().EnumUnderlyingType, astBuilder,
            out flattenedStruct);
        default:
          throw new Exception(String.Format("Non-unmanaged or unsupported type: {0}.", type));
      }
    }

    private int GetKnownTypeSize(KnownTypeCode typeCode)
    {
      switch (typeCode)
      {
        case KnownTypeCode.Boolean:
        case KnownTypeCode.Byte:
        case KnownTypeCode.SByte:
          return 1;
        case KnownTypeCode.UInt16:
        case KnownTypeCode.Int16:
        case KnownTypeCode.Char:
          return 2;
        case KnownTypeCode.UInt32:
        case KnownTypeCode.Int32:
        case KnownTypeCode.Single:
          return 4;
        case KnownTypeCode.UInt64:
        case KnownTypeCode.Int64:
        case KnownTypeCode.Double:
          return 8;
        case KnownTypeCode.UIntPtr:
        case KnownTypeCode.IntPtr:
          return is64Bit ? 8 : 4;
        default:
          throw new Exception(String.Format("Non-unmanaged or unsupported known type: {0}", typeCode));
      }
    }
    
    private bool IsFixedAllowed(AstType astType, CSharpAstResolver curResolver = null)
    {
      curResolver = curResolver ?? resolver;
      ResolveResult resolvedType = curResolver.Resolve(astType);
      if (resolvedType.IsError)
        throw new Exception(String.Format("Could not resolve member type {0} (resolved {1}, type {2})", 
          astType, resolvedType, resolvedType.Type));
      return IsFixedAllowed(resolvedType.Type);
    }
    
    private bool IsFixedAllowed(IType type)
    {
      /* "Fixed size buffer type must be one of the following: bool, byte, 
       *  short, int, long, char, sbyte, ushort, uint, ulong, float or double" */
      if (type.Kind != TypeKind.Struct)
        return false;
      KnownTypeCode c = type.GetDefinition().KnownTypeCode;
      return c == KnownTypeCode.Boolean ||
        c == KnownTypeCode.Byte   || c == KnownTypeCode.SByte ||
        c == KnownTypeCode.Int16  || c == KnownTypeCode.UInt16 ||
        c == KnownTypeCode.Int32  || c == KnownTypeCode.UInt32 ||
        c == KnownTypeCode.Int64  || c == KnownTypeCode.UInt64 ||
        c == KnownTypeCode.Single || c == KnownTypeCode.Double;
    }

    private TypeDeclaration GetStructDeclaration(IType type, out SyntaxTree fileRoot)
    {
      fileRoot = null;
      ITypeDefinition def = type.GetDefinition();
      if (def.BodyRegion.IsEmpty)
        return null;
      if (def.BodyRegion.FileName == filename)
        fileRoot = root;
      else if (!additionalFiles.TryGetValue(def.BodyRegion.FileName, out fileRoot))
        return null;
      return fileRoot.GetNodeContaining(def.BodyRegion.Begin, def.BodyRegion.End) as TypeDeclaration;
    }

    private int AlignToNext(int offset, int alignSize)
    {
      return offset + ((alignSize - (offset % alignSize)) % alignSize);
    }
    
    private TypeDeclaration FlattenStruct(TypeDeclaration s, IType type = null, 
      CSharpResolver resolverState = null)
    {
      bool isUnion = UnresolvedHasAttribute(s, "UnionAttribute");
      if (type != null)
        return FlattenStruct(s, isUnion, type, resolverState);
      else
        return FlattenStruct(s, isUnion, resolverState);
    }

    private TypeDeclaration FlattenStruct(TypeDeclaration s, bool isUnion, 
      CSharpResolver resolverState = null)
    {
      ResolveResult resolvedCurrentType = resolver.Resolve(s);
      if (resolvedCurrentType.IsError)
        throw new Exception(String.Format("Couldn't resolve type of struct {0}", s));
      return FlattenStruct(s, isUnion, resolvedCurrentType.Type, resolverState);
    }

    private class FlattenStructState
    {
      public TypeDeclaration orgStruct;
      public TypeDeclaration newStruct;
      public IType structType;
      public bool isUnion;
      public CSharpResolver resolverState;
      public TypeSystemAstBuilder astBuilder;
      public CSharpAstResolver localResolver;
      public CSharpAstResolver newResolver;
      public int size;
      public int largestMemberAlignmentSize;
      public StructLayoutAttribute structLayout;

      public int memberSize;
      public int curOffset;
      public int alignedOffset;
      public int fieldTypeSize;

      internal void UpdateResolver()
      {
        newResolver = new CSharpAstResolver(resolverState, newStruct);
      }
    }

    private TypeDeclaration FlattenStruct(TypeDeclaration s, bool isUnion, IType type, 
      CSharpResolver resolverState = null)
    {
      FlattenStructState state = new FlattenStructState();
      if (flattenedStructCache.TryGetValue(type, out state.newStruct))
        return state.newStruct;

      state.orgStruct = s;
      state.isUnion = isUnion;
      state.resolverState = resolverState;
      state.structType = type;

      if (resolverState != null)
        state.localResolver = new CSharpAstResolver(resolverState, s);
      else
        state.localResolver = resolver;
      if (state.resolverState == null)
        state.resolverState = state.localResolver.GetResolverStateBefore(s);

      state.astBuilder = new TypeSystemAstBuilder(state.resolverState);
      state.newStruct = (TypeDeclaration)s.Clone();
      state.newResolver = new CSharpAstResolver(state.resolverState, state.newStruct);

      state.structLayout = GetStructLayout(s, state.localResolver);
      if (state.isUnion && state.structLayout.Value != LayoutKind.Explicit)
      {
        state.structLayout = new StructLayoutAttribute(LayoutKind.Explicit) { 
          Size = state.structLayout.Size, Pack = state.structLayout.Pack };
      }

      EntityDeclaration curNewMember = GetFirstField(state.newStruct); //used as insertion point
      while (curNewMember != null)
      {
        curNewMember = FlattenMemberAndGotoNext(curNewMember, state);
      }

      //Update structlayout attribute
      if (state.structLayout.Size != 0)
        state.size = state.structLayout.Size;
      UpdateStructLayoutAttribute(state.newStruct, LayoutKind.Explicit, 1, state.size
        , state.astBuilder, 
        new CSharpAstResolver(state.localResolver.GetResolverStateBefore(s), state.newStruct));

      state.newStruct.AddAnnotation(
        new FlattenedStructAnnotation(state.size, state.largestMemberAlignmentSize));

      flattenedStructCache.Add(type, state.newStruct);
      return state.newStruct;
    }

    private EntityDeclaration FlattenMemberAndGotoNext(EntityDeclaration curNewMember, FlattenStructState s)
    {
      var ffd = curNewMember as FixedFieldDeclaration;
      var fd = curNewMember as FieldDeclaration;
      if (ffd == null && fd == null)
      {
        return GetNextField(curNewMember);
      }

      if (fd != null && fd.Variables.Count > 1)
      {
        curNewMember = SplitFieldPerVariable(fd, s.newStruct);
        s.UpdateResolver();
        return curNewMember;
      }
      else if (ffd != null && ffd.Variables.Count > 1)
      {
        curNewMember = SplitFixedFieldPerVariable(ffd);
        s.UpdateResolver();
        return curNewMember;
      }

      AstType fieldAstType = fd != null ? fd.ReturnType : ffd.ReturnType;
      ResolveResult fieldType = s.newResolver.Resolve(fieldAstType);
      if (fieldType.IsError)
        throw new Exception(String.Format("Could not resolve member type {0} (resolved {1}, type {2})", 
          fieldAstType, fieldType, fieldType.Type));
      TypeDeclaration inlineStruct;
      s.fieldTypeSize = ResolveTypeSize(fieldType.Type, s.astBuilder, out inlineStruct);

      if (s.isUnion)
        s.curOffset = 0;
      else if (s.structLayout.Value == LayoutKind.Explicit)
        s.curOffset = GetFieldOffsetAttribute(curNewMember, s.newResolver);
      else
        s.curOffset = s.size;

      if (fd != null)
      {
        s.memberSize = s.fieldTypeSize;
        if (inlineStruct == null)
        {
          s.largestMemberAlignmentSize = Math.Max(s.largestMemberAlignmentSize, s.fieldTypeSize);
          int effectivePack = Math.Min(s.structLayout.Pack, s.fieldTypeSize);
          s.alignedOffset = AlignToNext(s.curOffset, effectivePack);
          s.UpdateResolver();
          SetFieldOffsetAttribute(curNewMember, s.alignedOffset, s.astBuilder, s.newResolver);
          curNewMember = GetNextField(curNewMember);
        }
        else
        {
          curNewMember = ProcessNestedStructMember(curNewMember, inlineStruct, s);
        }
      }
      else
      {
        curNewMember = ProcessFixedField(ffd, inlineStruct, s);
      }

      if (s.structLayout.Value == LayoutKind.Explicit)
        s.size = Math.Max(s.size, s.alignedOffset + s.memberSize);
      else
        s.size = s.alignedOffset + s.memberSize;
      return curNewMember;
    }

    private EntityDeclaration ProcessFixedField(FixedFieldDeclaration ffd,
      TypeDeclaration inlineStruct, FlattenStructState s)
    {
      int count = (int)s.newResolver
        .Resolve(ffd.Variables.FirstOrNullObject().CountExpression).ConstantValue;
      if (!IsFixedAllowed(ffd.ReturnType, s.newResolver))
      {
        if (inlineStruct != null)
          throw new NotImplementedException("Struct arrays not yet supported!");
        s.memberSize = s.fieldTypeSize * count;
        s.alignedOffset = AlignToNext(s.curOffset, s.fieldTypeSize);
        var fieldStart = new FieldDeclaration();
        fieldStart.ReturnType = (AstType)ffd.ReturnType.Clone();
        fieldStart.Variables.Add(new VariableInitializer(GetFieldName(ffd) + "_Start"));
        fieldStart.Modifiers = ffd.Modifiers;
        ffd.ReplaceWith(fieldStart);
        var fieldRefProp = CreateReferenceProperty(s.newStruct, ffd, "");
        s.newStruct.Members.InsertAfter(fieldStart, fieldRefProp);
        s.UpdateResolver();
        SetFieldOffsetAttribute(fieldStart, s.alignedOffset, s.astBuilder, s.newResolver);
        return GetNextField(fieldStart);
      }
      else
      {
        s.memberSize = s.fieldTypeSize * count;
        s.alignedOffset = AlignToNext(s.curOffset, s.fieldTypeSize);
        SetFieldOffsetAttribute(ffd, s.alignedOffset, s.astBuilder, s.newResolver);
        return GetNextField(ffd);
      }
    }

    private EntityDeclaration ProcessNestedStructMember(EntityDeclaration curNewMember,
      TypeDeclaration inlineStruct, FlattenStructState s)
    {
      EntityDeclaration nextMember;
      int alignmentSize = inlineStruct.Annotation<FlattenedStructAnnotation>().LargestMemberAlignSize;
      s.largestMemberAlignmentSize = Math.Max(s.largestMemberAlignmentSize, alignmentSize);
      int effectivePack = Math.Min(s.structLayout.Pack, alignmentSize);
      s.alignedOffset = AlignToNext(s.curOffset, effectivePack);

      string memberName = GetFieldName(curNewMember);
      string startMemberName;
      EntityDeclaration startMember;
      bool isAnonymous = InlineNestedStructs && (
        UnresolvedHasAttribute(curNewMember, "AnonymousStructAttribute") || 
        UnresolvedHasAttribute(inlineStruct, "AnonymousStructAttribute"));
      if (InlineNestedStructs)
      {
        int i = 0;
        List<EntityDeclaration> newFields = new List<EntityDeclaration>();
        foreach (EntityDeclaration m in inlineStruct.Members)
        {
          if (!(m is FieldDeclaration) && !(m is FixedFieldDeclaration))
            continue;
         
          var newMember = (EntityDeclaration)m.Clone();
          if (!isAnonymous)
            SetFieldName(newMember, memberName + "_" + GetFieldName(m));
          if (i == 0)
            curNewMember.ReplaceWith(newMember);
          else
            s.newStruct.Members.InsertAfter(newFields[i-1], newMember);
          newFields.Add(newMember);
          i++;
        }
        s.UpdateResolver();
        foreach (EntityDeclaration m in newFields)
        {
          int offset = GetFieldOffsetAttribute(m, s.newResolver) + s.alignedOffset;
          SetFieldOffsetAttribute(m, offset, s.astBuilder, s.newResolver);
        }
        startMember = newFields[0];
        startMemberName = GetFieldName(startMember);
        nextMember = GetNextField(newFields[newFields.Count - 1]);
      }
      else
      {
        startMember = (EntityDeclaration)inlineStruct.Members.FirstOrNullObject().Clone();
        startMember.Modifiers = curNewMember.Modifiers | (startMember.Modifiers & Modifiers.Unsafe);
        startMemberName = GetFieldName(curNewMember) + "_Start";
        SetFieldName(startMember, startMemberName);
        curNewMember.ReplaceWith(startMember);
        s.UpdateResolver();
        SetFieldOffsetAttribute(startMember, s.alignedOffset, s.astBuilder, s.newResolver);
        nextMember = GetNextField(startMember);
      }

      if (!isAnonymous)
      {
        PropertyDeclaration refProp = CreateReferenceProperty(s.newStruct, curNewMember,
          "_Ref", startMemberName);
        s.newStruct.Members.InsertAfter(startMember, refProp);
      }
      return nextMember;
    }

    private PropertyDeclaration CreateReferenceProperty(TypeDeclaration @struct,
      EntityDeclaration curNewMember, string refNameSuffix, string startMemberName = null)
    {
      var prop = new PropertyDeclaration();
      prop.Modifiers = curNewMember.Modifiers;
      prop.Modifiers |= Modifiers.Unsafe;
      prop.Name = GetFieldName(curNewMember) + refNameSuffix;
      prop.Getter = new Accessor();
      prop.ReturnType = ((AstType)curNewMember.ReturnType.Clone()).MakePointerType();
      var body = new BlockStatement();
      var getStartMember = new PointerReferenceExpression();
      getStartMember.Target = new IdentifierExpression("__s");
      if (startMemberName == null)
        startMemberName = GetFieldName(curNewMember) + "_Start";
      getStartMember.MemberName = startMemberName;
      var startMemberRef = new UnaryOperatorExpression(UnaryOperatorType.AddressOf, getStartMember);
      var castExpression = new CastExpression((AstType)prop.ReturnType.Clone(), startMemberRef);
      var returnStatement = new ReturnStatement(castExpression);

      var fixedStatement = new FixedStatement();
      fixedStatement.Type = new SimpleType(@struct.Name).MakePointerType();
      fixedStatement.EmbeddedStatement = returnStatement;
      var variableInitializer = new VariableInitializer("__s", 
        new UnaryOperatorExpression(UnaryOperatorType.AddressOf, 
          new ThisReferenceExpression()));
      fixedStatement.Variables.Add(variableInitializer);
      body.Statements.Add(fixedStatement);
      prop.Getter.Body = body;
      return prop;
    }

    //Expects the EntityDeclaration to only define one variable.
    private void SetFieldName(EntityDeclaration field, string value)
    {
      if (field is FieldDeclaration)
      {
        ((FieldDeclaration)field).Variables
          .FirstOrNullObject().Name = value;
      }
      else if (field is FixedFieldDeclaration)
      {
        ((FixedFieldDeclaration)field).Variables
          .FirstOrNullObject().Name = value;
      }
    }

    //Expects the EntityDeclaration to only define one variable.
    private string GetFieldName(EntityDeclaration field)
    {
      if (field is FieldDeclaration)
      {
        return ((FieldDeclaration)field).Variables
          .FirstOrNullObject().Name;
      }
      else if (field is FixedFieldDeclaration)
      {
        return ((FixedFieldDeclaration)field).Variables
          .FirstOrNullObject().Name;
      }
      return null;
    }

    private FixedFieldDeclaration SplitFixedFieldPerVariable(FixedFieldDeclaration field)
    {
      throw new NotImplementedException();
    }

    //Returns the first new FieldDeclaration
    private FieldDeclaration SplitFieldPerVariable(FieldDeclaration field, TypeDeclaration @struct)
    {
      List<VariableInitializer> variables = field.Variables.ToList();
      FieldDeclaration first = null, last = field;
      for (int i = 0; i < variables.Count; i++)
      {
        var newField = (FieldDeclaration)field.Clone();
        if (i == 0)
          first = newField;
        newField.Variables.Clear();
        newField.Variables.AddRange(new VariableInitializer[] { 
          (VariableInitializer)variables[i].Clone() });
        if (i == 0)
          field.ReplaceWith(newField);
        else
          @struct.Members.InsertAfter(last, newField);
        last = newField;
      }
      return first;
    }

    private bool IsInstanceField(AstNode member)
    {
      var ed = member as EntityDeclaration;
      if (member == null)
        return false;
      return (ed is FieldDeclaration || ed is FixedFieldDeclaration) &&
          (ed.Modifiers & (Modifiers.Const | Modifiers.Static)) == Modifiers.None;
    }

    private EntityDeclaration GetFirstField(TypeDeclaration typeDeclaration)
    {
      return GetFirstFieldThisOrAfter(typeDeclaration.Members.FirstOrNullObject());
    }

    private EntityDeclaration GetFirstFieldThisOrAfter(AstNode member)
    {
      if (IsInstanceField(member))
        return (EntityDeclaration)member;
      return GetNextField(member);
    }

    private EntityDeclaration GetNextField(AstNode member)
    {
      AstNode curNode = member.NextSibling;
      while (curNode != null)
      {
        if (IsInstanceField(curNode))
        {
          return (EntityDeclaration)curNode;
        }
        curNode = curNode.NextSibling;
      }
      return null;
    }

    internal void AddFile(string filename)
    {
      TextReader tr = File.OpenText(filename);
      var fileRoot = parser.Parse(tr, filename);
      var fileTs = fileRoot.ToTypeSystem();
      pc = pc.AddOrUpdateFiles(fileTs);
      additionalFiles[filename] = fileRoot;
    }
  }
}
