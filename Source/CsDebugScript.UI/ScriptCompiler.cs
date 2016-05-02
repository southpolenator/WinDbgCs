﻿using CsDebugScript.Engine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CsDebugScript
{
    /// <summary>
    /// The result of script compilation
    /// </summary>
    public class CompileResult
    {
        /// <summary>
        /// Gets or sets the array of errors that happened during the compilation process.
        /// </summary>
        public CompileError[] Errors { get; set; }

        /// <summary>
        /// Gets or sets the compiled assembly.
        /// </summary>
        public Assembly CompiledAssembly { get; set; }
    }

    /// <summary>
    /// Error that happened during the compilation process.
    /// </summary>
    public class CompileError
    {
        /// <summary>
        /// Gets or sets the error number.
        /// </summary>
        public string ErrorNumber { get; set; }

        /// <summary>
        /// Gets or sets the full error message.
        /// </summary>
        public string FullMessage { get; set; }

        /// <summary>
        /// Gets or sets the name of the file.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the line.
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// Gets or sets the column.
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is warning.
        /// </summary>
        public bool IsWarning { get; set; }
    }

    internal class ScriptCompiler : IDisposable
    {
        /// <summary>
        /// The automatically generated namespace for the script
        /// </summary>
        internal const string AutoGeneratedNamespace = "AutoGeneratedNamespace";

        /// <summary>
        /// The automatically generated class name for the script
        /// </summary>
        internal const string AutoGeneratedClassName = "AutoGeneratedClassName";

        /// <summary>
        /// The automatically generated script function name
        /// </summary>
        internal const string AutoGeneratedScriptFunctionName = "ScriptFunction";

        /// <summary>
        /// The automatically generated script assembly name
        /// </summary>
        internal const string AutoGeneratedAssemblyName = "InteractiveScriptGeneratedAssembly.dll";

        /// <summary>
        /// The regex for code block comments
        /// </summary>
        internal const string CodeBlockComments = @"/\*(.*?)\*/";

        /// <summary>
        /// The regex for code line comments
        /// </summary>
        internal const string CodeLineComments = @"//(.*?)\r?\n";

        /// <summary>
        /// The regex for code strings
        /// </summary>
        internal const string CodeStrings = @"""((\\[^\n]|[^""\n])*)""";

        /// <summary>
        /// The regex for code verbatim strings
        /// </summary>
        internal const string CodeVerbatimStrings = @"@(""[^""]*"")+";

        /// <summary>
        /// The regex for code imports
        /// </summary>
        internal const string CodeImports = "import (([a-zA-Z][:])?([^\\/:*<>|;\"]+[\\/])*[^\\/:*<>|;\"]+);";

        /// <summary>
        /// The regex for code usings
        /// </summary>
        internal const string CodeUsings = "using ([^\";]+);";

        /// <summary>
        /// The compiled regex for removing comments
        /// </summary>
        internal static readonly Regex RegexRemoveComments = new Regex(CodeBlockComments + "|" + CodeLineComments + "|" + CodeStrings + "|" + CodeVerbatimStrings, RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// The compiled regex for extracting imports
        /// </summary>
        internal static readonly Regex RegexExtractImports = new Regex(CodeImports + "|" + CodeStrings + "|" + CodeVerbatimStrings, RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// The compiled regex for extracting usings
        /// </summary>
        internal static readonly Regex RegexExtractUsings = new Regex(CodeUsings + "|" + CodeStrings + "|" + CodeVerbatimStrings, RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Gets the list of search folders.
        /// </summary>
        internal List<string> SearchFolders { get; private set; }

        /// <summary>
        /// The code provider
        /// </summary>
        private CSharpCodeProvider codeProvider = new CSharpCodeProvider();

        /// <summary>
        /// The loaded assemblies
        /// </summary>
        private Dictionary<string, Assembly> loadedAssemblies = new Dictionary<string, Assembly>();

        /// <summary>
        /// The default assembly references used by the compiler
        /// </summary>
        internal static readonly string[] DefaultAssemblyReferences = GetDefaultAssemblyReferences();

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptCompiler"/> class.
        /// </summary>
        public ScriptCompiler()
        {
            SearchFolders = Context.Settings.SearchFolders;

            AppDomain.CurrentDomain.AssemblyResolve += AssemblyLoader;
        }

        /// <summary>
        /// Event handler for resolving (loading) assemblies.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The <see cref="ResolveEventArgs"/> instance containing the event data.</param>
        /// <returns>Resolved assembly based on assembly full name.</returns>
        private Assembly AssemblyLoader(object sender, ResolveEventArgs args)
        {
            Assembly result;

            if (loadedAssemblies.TryGetValue(args.Name, out result))
            {
                return result;
            }

            return null;
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        public void Dispose()
        {
            codeProvider.Dispose();
        }

        /// <summary>
        /// Gets the default assembly references used by the compiler.
        /// </summary>
        private static string[] GetDefaultAssemblyReferences()
        {
            dynamic justInitializationOfDynamics = new List<string>();
            List<string> assemblyReferences = new List<string>();

            assemblyReferences.Add(typeof(System.Object).Assembly.Location);
            assemblyReferences.Add(typeof(System.Linq.Enumerable).Assembly.Location);
            assemblyReferences.Add(typeof(CsDebugScript.Variable).Assembly.Location);

            // Check if Microsoft.CSharp.dll should be added to the list of referenced assemblies
            const string MicrosoftCSharpDll = "microsoft.csharp.dll";

            if (!assemblyReferences.Where(a => a.ToLowerInvariant().Contains(MicrosoftCSharpDll)).Any())
            {
                // TODO:
                var assembly = Assembly.LoadFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETCore\v4.5\Microsoft.CSharp.dll");
                assemblyReferences.AddRange(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.Location.ToLowerInvariant().Contains(MicrosoftCSharpDll)).Select(a => a.Location));
            }

            return assemblyReferences.ToArray();
        }

        /// <summary>
        /// Compiles the specified code.
        /// </summary>
        /// <param name="code">The code.</param>
        /// <param name="referencedAssemblies">The referenced assemblies.</param>
        protected CompileResult Compile(string code, params string[] referencedAssemblies)
        {
            // Temp folder for script debugging experience
            string tempDir = Path.GetTempPath() + @"\CsDebugScript\";

            Directory.CreateDirectory(tempDir);

#if USE_ROSLYN_COMPILER
            // Create references
            List<string> stringReferences = new List<string>();

            stringReferences.AddRange(DefaultAssemblyReferences);
            stringReferences.AddRange(referencedAssemblies);

            IEnumerable<MetadataReference> references = stringReferences.Distinct().Select(sr => MetadataReference.CreateFromFile(sr));

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);

#if X64
            Platform platform = Platform.X64;
#elif X86
            Platform platform = Platform.X86;
#else
            Platform platform = Platform.AnyCpu;
#endif
            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(AutoGeneratedAssemblyName),
                syntaxTrees: new SyntaxTree[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, platform: platform));

            using (var dllMemoryStream = new MemoryStream())
            using (var pdbMemoryStream = new MemoryStream())
            {
                var result = compilation.Emit(dllMemoryStream, pdbMemoryStream);

                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    List<CompileError> errors = new List<CompileError>();

                    foreach (Diagnostic diagnostic in failures)
                    {
                        string errorNumber = diagnostic.Descriptor.Id;
                        var lineSpan = diagnostic.Location.GetMappedLineSpan();
                        string fileName = lineSpan.Path;
                        int lineNumber = lineSpan.StartLinePosition.Line + 1;
                        int column = lineSpan.StartLinePosition.Character + 1;
                        string fullMessage = diagnostic.ToString();
                        bool isWarning = diagnostic.Severity == DiagnosticSeverity.Warning;

                        errors.Add(new CompileError()
                        {
                            Column = column,
                            Line = lineNumber,
                            ErrorNumber = errorNumber,
                            FileName = fileName,
                            FullMessage = fullMessage,
                            IsWarning = isWarning,
                        });
                    }

                    return new CompileResult()
                    {
                        Errors = errors.ToArray()
                    };
                }

                string tempAssemblyName = Guid.NewGuid() + AutoGeneratedAssemblyName;
                string dllFilename = Path.Combine(tempDir, tempAssemblyName);
                string pdbFilename = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(tempAssemblyName) + ".pdb");

                dllMemoryStream.Flush();
                pdbMemoryStream.Flush();
                using (var dllStream = new FileStream(dllFilename, FileMode.Create))
                using (var pdbStream = new FileStream(pdbFilename, FileMode.Create))
                {
                    var array = dllMemoryStream.ToArray();
                    dllStream.Write(array, 0, array.Length);
                    array = pdbMemoryStream.ToArray();
                    pdbStream.Write(array, 0, array.Length);
                }

                foreach (var referencedAssembly in referencedAssemblies)
                {
                    var assembly = Assembly.LoadFrom(referencedAssembly);
                    loadedAssemblies[assembly.FullName] = assembly;
                }

                return new CompileResult()
                {
                    CompiledAssembly = Assembly.LoadFile(dllFilename),
                    Errors = new CompileError[0],
                };
            }
#else
            var compilerParameters = new CompilerParameters()
            {
                IncludeDebugInformation = true,
                TempFiles = new TempFileCollection(tempDir, true),
            };

            compilerParameters.ReferencedAssemblies.AddRange(DefaultAssemblyReferences);
            compilerParameters.ReferencedAssemblies.AddRange(referencedAssemblies);

            // Compile the script
            CompilerResults results = codeProvider.CompileAssemblyFromSource(compilerParameters, code);

            if (results.Errors.Count == 0)
            {
                foreach (var referencedAssembly in referencedAssemblies)
                {
                    var assembly = Assembly.LoadFrom(referencedAssembly);
                    loadedAssemblies[assembly.FullName] = assembly;
                }
            }

            return new CompileResult()
            {
                CompiledAssembly = results.Errors.Count == 0 ? results.CompiledAssembly : null,
                Errors = results.Errors.Cast<CompilerError>().Select(s => new CompileError()
                {
                    Column = s.Column,
                    ErrorNumber = s.ErrorNumber,
                    FileName = s.FileName,
                    FullMessage = s.ErrorText,
                    IsWarning = s.IsWarning,
                    Line = s.Line,
                }).ToArray(),
            };
#endif
        }

        /// <summary>
        /// Gets the full path of the file.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="parentPaths">The array of parent paths.</param>
        protected string GetFullPath(string path, params string[] parentPaths)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            foreach (var pp in parentPaths)
            {
                string parentPath = pp;

                if (!string.IsNullOrEmpty(parentPath))
                {
                    if (File.Exists(parentPath))
                    {
                        parentPath = Path.GetDirectoryName(parentPath);
                    }

                    string newPath = Path.Combine(parentPath, path);

                    if (File.Exists(newPath))
                    {
                        return newPath;
                    }
                }
            }

            foreach (string folder in SearchFolders)
            {
                string newPath = Path.Combine(folder, path);

                if (File.Exists(newPath))
                {
                    return newPath;
                }
            }

            if (Path.GetExtension(path).ToLower() == ".dll")
            {
                string newPath = Path.Combine(Context.GetAssemblyDirectory(), path);

                if (File.Exists(newPath))
                {
                    return newPath;
                }
            }

            return path;
        }

        /// <summary>
        /// Loads the code from the script and imported files. It acts as precompiler.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="referencedAssemblies">The referenced assemblies.</param>
        /// <param name="defaultUsings">The array of default using namespaces. If null is supplied, it will be { System, System.Linq, CsDebygScript }</param>
        /// <returns>Merged code of all imported script files</returns>
        protected string LoadCode(string path, ISet<string> referencedAssemblies = null, string[] defaultUsings = null)
        {
            HashSet<string> loadedScripts = new HashSet<string>();
            HashSet<string> usings = new HashSet<string>(defaultUsings ?? new string[] { "System", "System.Linq", "CsDebugScript" });
            HashSet<string> imports = new HashSet<string>();
            StringBuilder importedCode = new StringBuilder();
            string fullPath = GetFullPath(path, Directory.GetCurrentDirectory());
            string scriptCode = ImportFile(path, usings, imports);

            loadedScripts.Add(path);
            while (imports.Count > 0)
            {
                HashSet<string> newImports = new HashSet<string>();

                foreach (string import in imports)
                {
                    if (!loadedScripts.Contains(import))
                    {
                        string extension = Path.GetExtension(import).ToLower();

                        if (extension == ".dll" || extension == ".exe")
                        {
                            if (referencedAssemblies != null)
                            {
                                referencedAssemblies.Add(import);
                            }
                        }
                        else
                        {
                            string code = ImportFile(import, usings, newImports);

                            importedCode.AppendLine(code);
                            loadedScripts.Add(import);
                        }
                    }
                }

                imports = newImports;
            }

            return GenerateCode(usings, importedCode.ToString(), scriptCode);
        }

        /// <summary>
        /// Generates the code based on parameters.
        /// </summary>
        /// <param name="usings">The usings.</param>
        /// <param name="importedCode">The imported code.</param>
        /// <param name="scriptCode">The script code.</param>
        /// <param name="scriptBaseClassName">Name of the script base class.</param>
        protected static string GenerateCode(IEnumerable<string> usings, string importedCode, string scriptCode, string scriptBaseClassName = "CsDebugScript.ScriptBase")
        {
            StringBuilder codeBuilder = new StringBuilder();

            foreach (var u in usings.OrderBy(a => a))
            {
                codeBuilder.Append("using ");
                codeBuilder.Append(u);
                codeBuilder.AppendLine(";");
            }

            codeBuilder.Append("namespace ");
            codeBuilder.AppendLine(AutoGeneratedNamespace);
            codeBuilder.AppendLine("{");
            codeBuilder.Append("public class ");
            codeBuilder.Append(AutoGeneratedClassName);
            codeBuilder.Append(" : ");
            codeBuilder.AppendLine(scriptBaseClassName);
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine(importedCode);
            codeBuilder.Append("public void ");
            codeBuilder.Append(AutoGeneratedScriptFunctionName);
            codeBuilder.AppendLine("(string[] args)");
            codeBuilder.AppendLine("{");
            codeBuilder.AppendLine(scriptCode);
            codeBuilder.AppendLine("}");
            codeBuilder.AppendLine("}");
            codeBuilder.AppendLine("}");
            return codeBuilder.ToString();
        }

        /// <summary>
        /// Imports the file.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="usings">The usings.</param>
        /// <param name="imports">The imports.</param>
        /// <returns>Code of the imported file</returns>
        protected string ImportFile(string path, ICollection<string> usings, ICollection<string> imports)
        {
            string code = File.ReadAllText(path);
            HashSet<string> localImports = new HashSet<string>();

            code = RemoveComments(code);
            code = ExtractImports(code, localImports);
            code = ExtractUsings(code, usings);
            foreach (string import in localImports)
            {
                imports.Add(GetFullPath(import, path));
            }

            return "#line 1 \"" + path + "\"\n" + code + "\n#line default\n";
        }

        /// <summary>
        /// Imports the code.
        /// </summary>
        /// <param name="code">The code.</param>
        /// <param name="usings">The usings.</param>
        /// <param name="imports">The imports.</param>
        /// <returns>Code without extracted usings, imports and comments</returns>
        protected string ImportCode(string code, ICollection<string> usings, ICollection<string> imports)
        {
            HashSet<string> localImports = new HashSet<string>();

            code = RemoveComments(code);
            code = ExtractImports(code, localImports);
            code = ExtractUsings(code, usings);
            foreach (string import in localImports)
            {
                imports.Add(GetFullPath(import));
            }

            return code;
        }

        /// <summary>
        /// Cleans the code for removal: replaces all non-newline characters with space.
        /// </summary>
        /// <param name="code">The code.</param>
        protected static string CleanCodeForRemoval(string code)
        {
            StringBuilder sb = new StringBuilder();

            foreach (char c in code)
                if (c == '\n')
                    sb.AppendLine();
                else
                    sb.Append(' ');
            return sb.ToString();
        }

        /// <summary>
        /// Removes the comments from the code.
        /// </summary>
        /// <param name="code">The code.</param>
        /// <returns>Code without comments.</returns>
        protected static string RemoveComments(string code)
        {
            return RegexRemoveComments.Replace(code,
                me =>
                {
                    if (me.Value.StartsWith("/*") || me.Value.StartsWith("//"))
                        return CleanCodeForRemoval(me.Value);
                    return me.Value;
                });
        }

        /// <summary>
        /// Extracts the imports from the code.
        /// </summary>
        /// <param name="code">The code.</param>
        /// <param name="imports">The imports.</param>
        /// <returns>Code without imports.</returns>
        protected static string ExtractImports(string code, ICollection<string> imports)
        {
            return RegexExtractImports.Replace(code,
                me =>
                {
                    if (me.Value.StartsWith("import"))
                    {
                        imports.Add(me.Groups[1].Value);
                        return CleanCodeForRemoval(me.Value);
                    }

                    return me.Value;
                });
        }

        /// <summary>
        /// Extracts the usings from the code.
        /// </summary>
        /// <param name="code">The code.</param>
        /// <param name="usings">The usings.</param>
        /// <returns>Code without usings.</returns>
        protected static string ExtractUsings(string code, ICollection<string> usings)
        {
            return RegexExtractUsings.Replace(code,
                me =>
                {
                    if (me.Value.StartsWith("using"))
                    {
                        usings.Add(me.Groups[1].Value);
                        return CleanCodeForRemoval(me.Value);
                    }

                    return me.Value;
                });
        }

        /// <summary>
        /// Extracts the metadata from user assemblies.
        /// </summary>
        /// <param name="assemblies">The assemblies.</param>
        internal static UserTypeMetadata[] ExtractMetadata(IEnumerable<Assembly> assemblies)
        {
            List<UserTypeMetadata> metadata = new List<UserTypeMetadata>();

            foreach (var assembly in assemblies)
            {
                List<Type> nextTypes = assembly.ExportedTypes.ToList();

                while (nextTypes.Count > 0)
                {
                    List<Type> types = nextTypes;

                    nextTypes = new List<Type>();
                    foreach (var type in types)
                    {
                        UserTypeMetadata[] userTypes = UserTypeMetadata.ReadFromType(type);

                        metadata.AddRange(userTypes);
                        nextTypes.AddRange(type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public));
                    }
                }
            }

            return metadata.ToArray();
        }
    }
}
