﻿using CommandLine;
using Dia2Lib;
using GenerateUserTypesFromPdb.UserTypes;
using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace GenerateUserTypesFromPdb
{
    class Options
    {
        [Option('p', "pdb", Required = true, HelpText = "Path to PDB which will be used to generate the code")]
        public string PdbPath { get; set; }

        [Option('t', "types", Separator = ',', Required = false, HelpText = "List of types to be exported", SetName = "cmdSettings")]
        public IList<string> Types { get; set; }

        [Option("no-type-info-comment", Default = false, HelpText = "Generate filed type info comment", Required = false, SetName = "cmdSettings")]
        public bool DontGenerateFieldTypeInfoComment { get; set; }

        [Option("multi-line-properties", Default = false, HelpText = "Generate properties as multi line", Required = false, SetName = "cmdSettings")]
        public bool MultiLineProperties { get; set; }

        [Option("use-dia-symbol-provider", Default = false, HelpText = "Use DIA symbol provider and access fields for specific type", Required = false, SetName = "cmdSettings")]
        public bool UseDiaSymbolProvider { get; set; }

        [Option("force-user-types-to-new-instead-of-casting", Default = false, HelpText = "Force using new during type casting instead of direct casting", Required = false, SetName = "cmdSettings")]
        public bool ForceUserTypesToNewInsteadOfCasting { get; set; }

        [Option("cache-user-type-fields", Default = false, HelpText = "Caches result of getting user type field when exporting user type", Required = false, SetName = "cmdSettings")]
        public bool CacheUserTypeFields { get; set; }

        [Option("cache-static-user-type-fields", Default = false, HelpText = "Caches result of getting static user type field when exporting user type", Required = false, SetName = "cmdSettings")]
        public bool CacheStaticUserTypeFields { get; set; }

        [Option("lazy-cache-user-type-fields", Default = false, HelpText = "Cache result of getting user type field inside UserMember when exporting user type", Required = false, SetName = "cmdSettings")]
        public bool LazyCacheUserTypeFields { get; set; }

        [Option("generate-physical-mapping-of-user-types", Default = false, HelpText = "Generate physical access to fields in exported user types (instead of symbolic/by name)", Required = false, SetName = "cmdSettings")]
        public bool GeneratePhysicalMappingOfUserTypes { get; set; }

        [Option("generated-assembly-name", Default = "", HelpText = "Name of the assembly that will be generated next to sources in output folder", Required = false, SetName = "cmdSettings")]
        public string GeneratedAssemblyName { get; set; }

        [Option("generated-props-file-name", Default = "", HelpText = "Name of the props file that will be generated next to sources in output folder. It can be later included into project that will be compiled", Required = false, SetName = "cmdSettings")]
        public string GeneratedPropsFileName { get; set; }

        [Option('x', "xml-config", HelpText = "Path to xml file with configuration", SetName = "xmlConfig")]
        public string XmlConfigPath { get; set; }
    }

    class Program
    {
        private static void OpenPdb(string path, out IDiaDataSource dia, out IDiaSession session)
        {
            dia = new DiaSource();
            dia.loadDataFromPdb(path);
            dia.openSession(out session);
        }

        static void Main(string[] args)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var error = Console.Error;
            Options options = null;

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o => options = o);

            if (options == null)
                return;

            XmlConfig config;

            if (!string.IsNullOrEmpty(options.XmlConfigPath))
            {
                config = XmlConfig.Read(options.XmlConfigPath);
            }
            else
            {
                config = new XmlConfig()
                {
                    DontGenerateFieldTypeInfoComment = options.DontGenerateFieldTypeInfoComment,
                    ForceUserTypesToNewInsteadOfCasting = options.ForceUserTypesToNewInsteadOfCasting,
                    MultiLineProperties = options.MultiLineProperties,
                    UseDiaSymbolProvider = options.UseDiaSymbolProvider,
                    GeneratedAssemblyName = options.GeneratedAssemblyName,
                    GeneratedPropsFileName = options.GeneratedPropsFileName,
                    CacheStaticUserTypeFields = options.CacheStaticUserTypeFields,
                    CacheUserTypeFields = options.CacheUserTypeFields,
                    LazyCacheUserTypeFields = options.LazyCacheUserTypeFields,
                    GeneratePhysicalMappingOfUserTypes = options.GeneratePhysicalMappingOfUserTypes,
                    Types = new XmlType[options.Types.Count],
                };

                for (int i = 0; i < options.Types.Count; i++)
                    config.Types[i] = new XmlType()
                    {
                        Name = options.Types[i],
                    };
            }

            string pdbPath = options.PdbPath;
            XmlType[] typeNames = config.Types;
            UserTypeGenerationFlags generationOptions = UserTypeGenerationFlags.None;

            if (!config.DontGenerateFieldTypeInfoComment)
                generationOptions |= UserTypeGenerationFlags.GenerateFieldTypeInfoComment;
            if (!config.MultiLineProperties)
                generationOptions |= UserTypeGenerationFlags.SingleLineProperty;
            if (config.UseDiaSymbolProvider)
                generationOptions |= UserTypeGenerationFlags.UseClassFieldsFromDiaSymbolProvider;
            if (config.ForceUserTypesToNewInsteadOfCasting)
                generationOptions |= UserTypeGenerationFlags.ForceUserTypesToNewInsteadOfCasting;
            if (config.CacheUserTypeFields)
                generationOptions |= UserTypeGenerationFlags.CacheUserTypeFields;
            if (config.CacheStaticUserTypeFields)
                generationOptions |= UserTypeGenerationFlags.CacheStaticUserTypeFields;
            if (config.LazyCacheUserTypeFields)
                generationOptions |= UserTypeGenerationFlags.LazyCacheUserTypeFields;
            if (config.GeneratePhysicalMappingOfUserTypes)
                generationOptions |= UserTypeGenerationFlags.GeneratePhysicalMappingOfUserTypes;
            if (config.SingleFileExport)
                generationOptions |= UserTypeGenerationFlags.SingleFileExport;

            string moduleName = Path.GetFileNameWithoutExtension(pdbPath).ToLower();
            var factory = new UserTypeFactory(config.Transformations);
            IDiaDataSource dia;
            IDiaSession session;

            OpenPdb(pdbPath, out dia, out session);

            Module module = new Module(moduleName, session);

            foreach (var type in typeNames)
            {
                Symbol[] symbols = module.FindGlobalTypeWildcard(type.NameWildcard);

                if (symbols.Length == 0)
                {
                    error.WriteLine("Symbol not found: {0}", type.Name);
                }
                else
                {
                    factory.AddSymbols(session, symbols, type, moduleName, generationOptions);
                }
            }

            Dictionary<string, List<Symbol>> templateSymbols = new Dictionary<string, List<Symbol>>();
            Dictionary<string, Symbol> specializedClassWithParentSymbol = new Dictionary<string, Symbol>();

            Console.Write("Enumerating all types... ");
            var globalTypes = module.GetAllTypes();
            Console.WriteLine(sw.Elapsed);

            foreach (Symbol symbol in globalTypes)
            {
                //  TODO add configurable filter
                //
                string symbolName = symbol.Name;
                if (symbolName.StartsWith("$") || symbolName.StartsWith("__vc_attributes") || symbolName.StartsWith("`anonymous-namespace'"))
                {
                    continue;
                }

                // Do not handle template referenced arguments 
                if (symbolName.Contains("&"))
                {
                    continue;
                }

                // Skip symbols with large names (filepath issue)
                if (symbolName.Length > 160)
                {
                    continue;
                }

                var namespaces = NameHelper.GetFullSymbolNamespaces(symbolName);

                string scopedClassName = NameHelper.GetSymbolScopedClassName(symbolName);

                if (scopedClassName == "<>")
                {
                    // TODO
                    // for now remove all unnamed-type symbols
                    //
                    continue;
                }

                // Parent Class is Template, Nested is Physical
                // Check if dealing template type.
                if (NameHelper.ContainsTemplateType(symbolName))
                {
                    if (!NameHelper.IsTemplateType(scopedClassName))
                    {
                        // Parent is template but class itself is not,
                        // Class needs to be aware of parent context (UserTypeFactory).
                        //
                        // TODO
                        symbolName = string.Format("{0}:{1}", NameHelper.GetLookupNameForSymbol(symbolName), scopedClassName);
                        //specializedClassWithParentSymbol.TryAdd(symbolName, symbol);
                        continue;
                    }
                    else
                    {
                        try
                        {
                            string className = namespaces.Last();

                            List<string> templateSpecializationArgs = NameHelper.GetTemplateSpecializationArguments(className);

                            //
                            // TODO
                            // Inspect Template
                            //
                            TemplateUserType templateType = new TemplateUserType(symbol, new XmlType() { Name = symbolName }, moduleName, factory);

                            int templateArgs = templateType.GenericsArguments;
                            if (templateSpecializationArgs.Any(r => r == "void" || r == "void const"))
                            {
                                GlobalCache.DiaSymbolsByName.TryAdd(symbolName, symbol);
                            }

                            symbolName = NameHelper.GetLookupNameForSymbol(symbol);

                            if (templateSymbols.ContainsKey(symbolName) == false)
                            {
                                templateSymbols[symbolName] = new List<Symbol>() { symbol };
                            }
                            else
                            {
                                templateSymbols[symbolName].Add(symbol);
                            }

                            //
                            // TO DO
                            // Do not add physical types for template specialization (not now)
                            // do if types contains static fields
                            // nested in templates
                            continue;
                        }
                        catch(Exception ex)
                        {
                            continue;
                        }
                    }
                }

                GlobalCache.DiaSymbolsByName.TryAdd(symbolName, symbol);
            }

            Console.WriteLine("Collecting types: {0}", sw.Elapsed);

            // Populate specialization first
            //
            foreach (Symbol symbol in specializedClassWithParentSymbol.Values)
            {
                string symbolName = symbol.Name;

                XmlType type = new XmlType()
                {
                    Name = symbolName
                };

                factory.AddSymbol(symbol, type, moduleName, generationOptions);
            }

            Console.WriteLine("Populating specializations: {0}", sw.Elapsed);

            // Populate Templates
            //
            foreach (List<Symbol> symbols in templateSymbols.Values)
            {
                string symbolName = NameHelper.GetLookupNameForSymbol(symbols.First());

                //
                //  TODO
                //  consider adding physical type when dealing with single specialization
                //  revisit after adding multiple pdb support
                //
                try
                {

                    XmlType type = new XmlType()
                    {
                        Name = symbolName
                    };

                    factory.AddSymbols(session, symbols, type, moduleName, generationOptions);
                }
                catch(Exception)
                {
                    //  failed to add template type
                    //
                    //  TODO
                    //  consider adding specialized types
                    //
                }
            }

            Console.WriteLine("Populating templates: {0}", sw.Elapsed);

            //   Specialized class
            //
            foreach (Symbol symbol in GlobalCache.DiaSymbolsByName.Values)
            {
                string symbolName = symbol.Name;

                XmlType type = new XmlType()
                {
                    Name = symbolName
                };

                factory.AddSymbol(symbol, type, moduleName, generationOptions);
            }


            Console.WriteLine("Populating specialized classes: {0}", sw.Elapsed);

            //  To solve template dependencies.
            //  Update specialization arguments once all the templates has been populated.
            //
            foreach (TemplateUserType templateUserType in GlobalCache.UserTypesBySymbolName.Values.OfType<TemplateUserType>())
            {
                templateUserType.UpdateArguments(factory);
            }

            Console.WriteLine("Updating template arguments: {0}", sw.Elapsed);

            // Update 
            UserType[] userTypesInitialSet = factory.Symbols.ToArray();
            foreach (UserType userType in userTypesInitialSet)
            {
                userType.UpdateUserTypes(factory, generationOptions);
            }

            Console.WriteLine("Updating user types: {0}", sw.Elapsed);

            factory.ProcessTypes();
            factory.InserUserType(new GlobalsUserType(module, moduleName));

            Console.WriteLine("Post processing user types: {0}", sw.Elapsed);

            string currentDirectory = Directory.GetCurrentDirectory();
            string outputDirectory = currentDirectory + "\\output\\";
            Directory.CreateDirectory(outputDirectory);


            ConcurrentDictionary<string, string> generatedFiles = new ConcurrentDictionary<string, string>();

            string[] allUDTs = session.globalScope.GetChildren(SymTagEnum.SymTagUDT).Select(s => s.name).Distinct().OrderBy(s => s).ToArray();

            File.WriteAllLines(outputDirectory + "symbols.txt", allUDTs);

            if (!config.SingleFileExport)
            {
                // Generate Code
                Parallel.ForEach(factory.Symbols,
                    (symbolEntry) =>
                    {
                        GenerateUseTypeCode(symbolEntry, factory, outputDirectory, error, generationOptions, generatedFiles);
                    });
            }
            else
            {
                string filename = string.Format(@"{0}\{1}_all.exported.cs", outputDirectory, moduleName);
                HashSet<string> usings = new HashSet<string>();
                foreach (var symbolEntry in factory.Symbols)
                    foreach (var u in symbolEntry.Usings)
                        usings.Add(u);

                generatedFiles.TryAdd(filename.ToLowerInvariant(), filename);
                using (TextWriter masterOutput = new StreamWriter(filename, false /* append */, System.Text.Encoding.ASCII, 8192))
                {
                    foreach (var u in usings.OrderBy(s => s))
                        masterOutput.WriteLine("using {0};\n", u);
                    masterOutput.WriteLine();

                    Parallel.ForEach(factory.Symbols,
                        (symbolEntry) =>
                        {
                            using (StringWriter output = new StringWriter())
                            {
                                GenerateUseTypeCodeInSingleFile(output, symbolEntry, factory, error, generationOptions);
                                lock (masterOutput)
                                {
                                    masterOutput.WriteLine(output.ToString());
                                }
                            }
                        });
                }
            }

            Console.WriteLine("Saving code to disk: {0}", sw.Elapsed);

            if (!string.IsNullOrEmpty(config.GeneratedPropsFileName))
            {
                using (TextWriter output = new StreamWriter(outputDirectory + config.GeneratedPropsFileName))
                {
                    output.WriteLine(@"<?xml version=""1.0"" encoding=""utf-8""?>");
                    output.WriteLine(@"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">");
                    output.WriteLine(@"  <ItemGroup>");
                    foreach (var file in generatedFiles.Values)
                        output.WriteLine(@"    <Compile Include=""{0}"" />", file);
                    output.WriteLine(@" </ItemGroup>");
                    output.WriteLine(@"</Project>");
                }
            }

            // Check whether we should generate assembly
            if (!string.IsNullOrEmpty(config.GeneratedAssemblyName))
            {
                var codeProvider = new CSharpCodeProvider();
                var compilerParameters = new CompilerParameters()
                {
                    IncludeDebugInformation = true,
                    OutputAssembly = outputDirectory + config.GeneratedAssemblyName,
                };

                compilerParameters.ReferencedAssemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.Location).ToArray());
                //compilerParameters.ReferencedAssemblies.AddRange(referencedAssemblies);

                const string MicrosoftCSharpDll = "Microsoft.CSharp.dll";

                if (!compilerParameters.ReferencedAssemblies.Cast<string>().Where(a => a.Contains(MicrosoftCSharpDll)).Any())
                {
                    compilerParameters.ReferencedAssemblies.Add(MicrosoftCSharpDll);
                }

                string binFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                compilerParameters.ReferencedAssemblies.Add(Path.Combine(binFolder, "CsScriptManaged.dll"));
                compilerParameters.ReferencedAssemblies.Add(Path.Combine(binFolder, "CsScripts.CommonUserTypes.dll"));

                var compileResult = codeProvider.CompileAssemblyFromFile(compilerParameters, generatedFiles.Values.ToArray());

                if (compileResult.Errors.Count > 0)
                {
                    Console.Error.WriteLine("Compile errors:");
                    foreach (CompilerError err in compileResult.Errors)
                        Console.Error.WriteLine(err);
                }

                Console.WriteLine("Compiling: {0}", sw.Elapsed);
            }

            Console.WriteLine("Total time: {0}", sw.Elapsed);
        }

        private static ConcurrentDictionary<string, string> createdDirectories = new ConcurrentDictionary<string, string>();

        private static bool GenerateUseTypeCode(UserType userType, UserTypeFactory factory, string outputDirectory, TextWriter errorOutput, UserTypeGenerationFlags generationOptions, ConcurrentDictionary<string, string> generatedFiles)
        {
            Symbol symbol = userType.Symbol;

            if (symbol.Tag == SymTagEnum.SymTagBaseType)
            {
                // ignore Base (Primitive) types.
                return false;
            }

            if (userType.DeclaredInType != null)
            {
                return false;
            }

            try
            {
                string classOutputDirectory = outputDirectory;

                classOutputDirectory = Path.Combine(classOutputDirectory, userType.ModuleName);

                if (!string.IsNullOrEmpty(userType.Namespace))
                    classOutputDirectory = Path.Combine(classOutputDirectory, UserType.NormalizeSymbolName(UserType.NormalizeSymbolName(userType.Namespace).Replace(".", "\\").Replace(":", ".")));


                string ss;
                if (!createdDirectories.TryGetValue(classOutputDirectory, out ss))
                {
                    Directory.CreateDirectory(classOutputDirectory);
                    createdDirectories.TryAdd(classOutputDirectory, classOutputDirectory);
                }

                bool isEnum = userType is EnumUserType;

                string filename = string.Format(@"{0}\{1}{2}.exported.cs", classOutputDirectory, userType.ConstructorName, isEnum ? "_enum" : "");

                int index = 1;
                while (true)
                {
                    if (generatedFiles.TryAdd(filename.ToLowerInvariant(), filename))
                    {
                        break;
                    }

                    filename = string.Format(@"{0}\{1}_{2}.exported.cs", classOutputDirectory, userType.ConstructorName, index++);
                }

                //using (StringWriter output = new StringWriter())
                using (TextWriter output = new StreamWriter(filename, false /* append */, System.Text.Encoding.ASCII, 8192))
                {
                    userType.WriteCode(new IndentedWriter(output), errorOutput, factory, generationOptions);
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        private static bool GenerateUseTypeCodeInSingleFile(TextWriter output, UserType userType, UserTypeFactory factory, TextWriter errorOutput, UserTypeGenerationFlags generationOptions)
        {
            Symbol symbol = userType.Symbol;

            if (symbol.Tag == SymTagEnum.SymTagBaseType)
            {
                // ignore Base (Primitive) types.
                return false;
            }

            if (userType.DeclaredInType != null)
            {
                return false;
            }

            try
            {
                userType.WriteCode(new IndentedWriter(output), errorOutput, factory, generationOptions);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }
}
