/*
    Copyright 2010 MCLawl Team - Written by Valek (Modified by Supernova)

    Edited for use with Supernova
 
    Dual-licensed under the Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Supernova.Scripting {
    
    /// <summary> Utility methods for loading assemblies, commands, and plugins </summary>
    public static class IScripting {
        
        public const string AutoloadFile = "text/cmdautoload.txt";
        public const string DllDir = "extra/commands/dll/";
        
        /// <summary> Returns the default .dll path for the custom command with the given name </summary>
        public static string CommandPath(string name) { return DllDir + "Cmd" + name + ".dll"; }
        /// <summary> Returns the default .dll path for the plugin with the given name </summary>
        public static string PluginPath(string name)  { return "plugins/" + name + ".dll"; }
        
        /// <summary> Constructs instances of all types which derive from T in the given assembly. </summary>
        /// <returns> The list of constructed instances. </returns>
        public static List<T> LoadTypes<T>(Assembly lib) {
            List<T> instances = new List<T>();
            
            foreach (Type t in lib.GetTypes()) {
                if (t.IsAbstract || t.IsInterface || !t.IsSubclassOf(typeof(T))) continue;
                object instance = Activator.CreateInstance(t);
                
                if (instance == null) {
                    Logger.Log(LogType.Warning, "{0} \"{1}\" could not be loaded", typeof(T).Name, t.Name);
                    throw new BadImageFormatException();
                }
                instances.Add((T)instance);
            }
            return instances;
        }
        
        static byte[] GetDebugPDB(string path) {
            path = Path.ChangeExtension(path, ".pdb");
            if (!File.Exists(path)) return null;
            
            try {
                return File.ReadAllBytes(path);
            } catch (Exception ex) {
                Logger.LogError("Error loading .pdb " + path, ex);
                return null;
            }
        }
        
        /// <summary> Loads the given assembly from disc (and associated .pdb debug data) </summary>
        public static Assembly LoadAssembly(string path) {
            byte[] data  = File.ReadAllBytes(path);
            byte[] debug = GetDebugPDB(path);
            return Assembly.Load(data, debug);
        }
        
        
        public static void AutoloadCommands() {
            if (!File.Exists(AutoloadFile)) { File.Create(AutoloadFile); return; }
            string[] list = File.ReadAllLines(AutoloadFile);
            
            foreach (string cmdName in list) {
                if (cmdName.IsCommentLine()) continue;
                string path  = CommandPath(cmdName);
                string error = LoadCommands(path);
                
                if (error != null) { Logger.Log(LogType.Warning, error); continue; }
                Logger.Log(LogType.SystemActivity, "AUTOLOAD: Loaded Cmd{0}.dll", cmdName);
            }
        }
        
        /// <summary> Loads and registers all the commands from the given .dll path. </summary>
        public static string LoadCommands(string path) {
            try {
                Assembly lib = LoadAssembly(path);
                List<Command> commands = LoadTypes<Command>(lib);
                
                if (commands.Count == 0) return "No commands in dll file";
                foreach (Command cmd in commands) { Command.Register(cmd); }
            } catch (Exception ex) {
                Logger.LogError("Error loading commands from " + path, ex);
                
                string file = Path.GetFileName(path);
                if (ex is FileNotFoundException) {
                    return file + " does not exist in the DLL folder, or is missing a dependency. Details in the error log.";
                } else if (ex is BadImageFormatException) {
                    return file + " is not a valid assembly, or has an invalid dependency. Details in the error log.";
                } else if (ex is FileLoadException) {
                    return file + " or one of its dependencies could not be loaded. Details in the error log.";
                }
                return "An unknown error occured. Details in the error log.";
            }
            return null;
        }
        
        
        public static void AutoloadPlugins() {
            string[] files = AtomicIO.TryGetFiles("plugins", "*.dll");
            
            if (files != null) {
                foreach (string path in files) { LoadPlugin(path, true); }
            } else {
                Directory.CreateDirectory("plugins");
            }
        }
        
        /// <summary> Loads all plugins from the given .dll path. </summary>
        public static bool LoadPlugin(string path, bool auto) {
            try {
                Assembly lib = LoadAssembly(path);
                List<Plugin> plugins = IScripting.LoadTypes<Plugin>(lib);
                
                foreach (Plugin plugin in plugins) {
                    if (!Plugin.Load(plugin, auto)) return false;
                }
                return true;
            } catch (Exception ex) {
                Logger.LogError("Error loading plugins from " + path, ex);
                return false;
            }
        }
    }
    
    /// <summary> Compiles source code files for a particular programming language into a .dll </summary>
    public abstract class ICompiler {
        
        public const string SourceDir = "extra/commands/source/";
        public const string ErrorPath = "logs/errors/compiler.log";
        
        /// <summary> Default file extension used for source code files </summary>
        /// <example> .cs, .vb </example>
        public abstract string FileExtension { get; }
        /// <summary> The short name of this programming language </summary>
        /// <example> CS, VB </example>
        public abstract string ShortName { get; }
        /// <summary> The full name of this programming language </summary>
        /// <example> CSharp, Visual Basic </example>
        public abstract string FullName { get; }
        /// <summary> Returns source code for an example Command </summary>
        public abstract string CommandSkeleton { get; }
        /// <summary> Returns source code for an example Plugin </summary>
        public abstract string PluginSkeleton { get; }
        
        public string CommandPath(string name) { return SourceDir + "Cmd" + name + FileExtension; }
        public string PluginPath(string name)  { return "plugins/" + name + FileExtension; }
        
        /// <summary> C# compiler instance. </summary>
        public static ICompiler CS = new CSCompiler();
        /// <summary> Visual Basic compiler instance. </summary>
        public static ICompiler VB = new VBCompiler();
        
        public static List<ICompiler> Compilers = new List<ICompiler>() { CS, VB };
        
        
        public static ICompiler Lookup(string name, Player p) {
            if (name.Length == 0) return Compilers[0];
            
            foreach (ICompiler comp in Compilers) {
                if (comp.ShortName.CaselessEq(name)) return comp;
            }
            
            p.Message("&WUnknown language \"{0}\"", name);
            p.Message("&HAvailable languages: &f{0}",
                      Compilers.Join(c => c.ShortName + " (" + c.FullName + ")"));
            return null;
        }

        static string FormatSource(string source, params string[] args) {
            // Always use \r\n line endings so it looks correct in Notepad
            source = source.Replace(@"\t", "\t");
            source = source.Replace("\n", "\r\n");
            return string.Format(source, args);
        }
        
        public string GenExampleCommand(string cmdName) {
            cmdName = cmdName.ToLower().Capitalize();
            return FormatSource(CommandSkeleton, cmdName);
        }
        
        public string GenExamplePlugin(string plugin, string creator) {
            return FormatSource(PluginSkeleton, plugin, creator, Server.Version);
        }
        
        
        const int maxLog = 2;
        /// <summary> Attempts to compile the given source code file to a .dll file. </summary>
        /// <remarks> If dstPath is null, compiles to an in-memory .dll instead. </remarks>
        /// <remarks> Logs errors to IScripting.ErrorPath. </remarks>
        public CompilerResults Compile(string srcPath, string dstPath) {
            return Compile(new [] { srcPath }, dstPath);
        }
        
        /// <summary> Attempts to compile the given source code files to a .dll file. </summary>
        /// <remarks> If dstPath is null, compiles to an in-memory .dll instead. </remarks>
        /// <remarks> Logs errors to IScripting.ErrorPath. </remarks>
        public CompilerResults Compile(string[] srcPaths, string dstPath) {
            CompilerResults results = DoCompile(srcPaths, dstPath);
            if (!results.Errors.HasErrors) return results;
            
            SourceMap sources = new SourceMap(srcPaths);
            StringBuilder sb  = new StringBuilder();
            sb.AppendLine("############################################################");
            sb.AppendLine("Errors when compiling " + srcPaths.Join());
            sb.AppendLine("############################################################");
            sb.AppendLine();
            
            foreach (CompilerError err in results.Errors) {
                string type = err.IsWarning ? "Warning" : "Error";
                sb.AppendLine(type + " on line " + err.Line + ":");
                
                if (err.Line > 0) sb.AppendLine(sources.Get(err.FileName, err.Line - 1));
                if (err.Column > 0) sb.Append(' ', err.Column - 1);
                sb.AppendLine("^-- " + type + " #" + err.ErrorNumber + " - " + err.ErrorText);
                
                sb.AppendLine();
                sb.AppendLine("-------------------------");
                sb.AppendLine();
            }
            
            using (StreamWriter w = new StreamWriter(ErrorPath, true)) {
                w.Write(sb.ToString());
            }
            return results;
        }
        
        /// <summary> Compiles the given source code. </summary>
        protected abstract CompilerResults DoCompile(string[] srcPaths, string dstPath);
        
        
        public bool TryCompile(Player p, string type, string[] srcs, string dst) {
            CompilerResults results = Compile(srcs, dst);
            if (!results.Errors.HasErrors) {
                p.Message("{0} compiled successfully.", type);
                return true;
            }
            
            SummariseErrors(results, p);
            p.Message("&WCompilation error. See " + ErrorPath + " for more information.");
            return false;
        }
        
        /// <summary> Messages a summary of warnings and errors to the given player. </summary>
        public static void SummariseErrors(CompilerResults results, Player p) {
            int logged = 0;
            foreach (CompilerError err in results.Errors) {
                string type = err.IsWarning ? "Warning" : "Error";
                p.Message("&W{0} #{1} on line {2} - {3}", type, err.ErrorNumber, err.Line, err.ErrorText);
                
                logged++;
                if (logged >= maxLog) break;
            }
            
            if (results.Errors.Count <= maxLog) return;
            p.Message(" &W.. and {0} more", results.Errors.Count - maxLog);
        }
    }
    
    /// <summary> Compiles source code files from a particular language into a .dll file, using a CodeDomProvider for the compiler. </summary>
    public abstract class ICodeDomCompiler : ICompiler {
        readonly object compilerLock = new object();
        CodeDomProvider compiler;
        
        /// <summary> Creates a CodeDomProvider instance for this programming language </summary>
        protected abstract CodeDomProvider CreateProvider();
        /// <summary> Adds language-specific default arguments to list of arguments. </summary>
        protected abstract void PrepareArgs(CompilerParameters args);
        /// <summary> Returns the prefix for an assembly reference line </summary>
        /// <example> For C# this prefix is "//reference "" </example>
        protected virtual string ReferenceLine { get { return "//reference "; } }
        
        // Lazy init compiler when it's actually needed
        void InitCompiler() {
            lock (compilerLock) {
                if (compiler != null) return;
                compiler = CreateProvider();
                if (compiler != null) return;
                
                Logger.Log(LogType.Warning,
                           "WARNING: {0} compiler is missing, you will be unable to compile {1} files.",
                           FullName, FileExtension);
                // TODO: Should we log "You must have .net developer tools. (You need a visual studio)" ?
            }
        }
        
        void AddReferences(string path, CompilerParameters args) {
            // Allow referencing other assemblies using '//reference [assembly name]' at top of the file
            using (StreamReader r = new StreamReader(path)) {               
                string refPrefix = ReferenceLine;
                string line;
                
                while ((line = r.ReadLine()) != null) {
                    if (!line.CaselessStarts(refPrefix)) break;
                    
                    int index = line.IndexOf(' ') + 1;
                    // For consistency with C#, treat '//reference X.dll;' as '//reference X.dll'
                    string assem = line.Substring(index).Replace(";", "");
                    
                    args.ReferencedAssemblies.Add(assem);
                }
            }
        }
        
        protected override CompilerResults DoCompile(string[] srcPaths, string dstPath) {
            CompilerParameters args = new CompilerParameters();
            args.GenerateExecutable      = false;
            args.IncludeDebugInformation = true;
            
            if (dstPath != null) args.OutputAssembly   = dstPath;
            if (dstPath == null) args.GenerateInMemory = true;
            
            for (int i = 0; i < srcPaths.Length; i++) {
                // CodeDomProvider doesn't work properly with relative paths
                string path = Path.GetFullPath(srcPaths[i]);
                
                AddReferences(path, args);
                srcPaths[i] = path;
            }
            args.ReferencedAssemblies.Add("Supernova_.dll");
            
            PrepareArgs(args);
            InitCompiler();
            return compiler.CompileAssemblyFromFile(args, srcPaths);
        }
    }
    
    class SourceMap {
        readonly string[] files;
        readonly List<string>[] sources;
        
        public SourceMap(string[] paths) {
            files   = paths;
            sources = new List<string>[paths.Length];
        }
        
        int FindFile(string file) {
            for (int i = 0; i < files.Length; i++) {
                if (file.CaselessEq(files[i])) return i;
            }
            return -1;
        }
        
        /// <summary> Returns the given line in the given source code file </summary>
        public string Get(string file, int line) {
            int i = FindFile(file);
            if (i == -1) return "";
            
            List<string> source = sources[i];
            if (source == null) {
                try {
                    source = Utils.ReadAllLinesList(file);
                } catch {
                    source = new List<string>();
                }
                sources[i] = source;
            }            
            return line < source.Count ? source[line] : "";
        }
    }
}
