using System;
using System.IO;

namespace computegen
{
    class Program
    {
        static void Main(string[] args)
        {
            var rootDir = GetRepoRootDirectory();
            var distDir = Path.Combine(rootDir, "dist");

            var rhinocommonPath = Path.Combine(rootDir, "..", "rhino", "src4", "DotNetSDK", "rhinocommon", "dotnet");
            Console.WriteLine("[BEGIN PARSE]");
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            ClassBuilder.BuildClassDictionary(rhinocommonPath);
            Console.ResetColor();
            Console.WriteLine("[END PARSE]");

            string[] filter = new string[] {
                ".AreaMassProperties",
                ".BezierCurve",
                ".Brep", ".BrepFace",
                ".Curve", ".Extrusion", ".GeometryBase", ".Intersection", ".Mesh",
                ".NurbsCurve", ".NurbsSurface", ".SubD", ".Surface",
                ".VolumeMassProperties"
            };

            // var di = SharedRepoDirectory();
            // return;

            Console.ForegroundColor = ConsoleColor.Blue;
            var classes = ClassBuilder.FilteredList(ClassBuilder.AllClasses, filter);

            // Javascript
            Console.WriteLine("Writing javascript client");
            var js = new JavascriptClient();

            string jsDir = Path.Combine(distDir, "javascript");
            Console.WriteLine(jsDir);
            Directory.CreateDirectory(jsDir);
            string javascriptPath = Path.Combine(jsDir, "compute.rhino3d.js");
            // return;

            // if( di!=null)
            // {
            //     string dir = Path.Combine(di.FullName, "computeclient_js");
            //     if (Directory.Exists(dir))
            //         javascriptPath = Path.Combine(dir, javascriptPath);
            // }
            js.Write(ClassBuilder.AllClasses, javascriptPath, filter);
            DirectoryInfo jsdocDirectory = Directory.CreateDirectory(Path.Combine(distDir, "javascript", "docs"));
            Console.WriteLine(jsdocDirectory.FullName);

            // if( di!=null )
            // {
            //     string dir = Path.Combine(di.FullName, "computeclient_js", "docs");
            //     if (Directory.Exists(dir))
            //         jsdocDirectory = new DirectoryInfo(dir);
            // }
            Console.WriteLine("Writing javascript docs");
            RstClient.WriteJavascriptDocs(classes, jsdocDirectory);

            // Python
            Console.WriteLine("Writing python client");
            string basePythonDirectory = Path.Combine(distDir, "python");
            Directory.CreateDirectory(basePythonDirectory);
            Console.WriteLine(basePythonDirectory);
            // if(di != null)
            // {
            //     string dir = Path.Combine(di.FullName, "computeclient_py");
            //     if (Directory.Exists(dir))
            //         basePythonDirectory = dir;
            // }
            var py = new PythonClient();
            py.Write(ClassBuilder.AllClasses, basePythonDirectory, filter);
            Console.WriteLine("Writing python docs");
            RstClient.WritePythonDocs(basePythonDirectory, classes);

            // C#
            Console.WriteLine("Writing C# client");
            var cs = new DotNetClient();
            var csDir = Path.Combine(distDir, "dotnet");
            Directory.CreateDirectory(csDir);
            cs.Write(ClassBuilder.AllClasses, Path.Combine(csDir, "RhinoCompute.cs"), filter);


            Console.ResetColor();
        }

        static string GetRepoRootDirectory()
        {
            var exeDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            var srcbin = string.Format("{0}src{0}bin{0}", Path.DirectorySeparatorChar);
            var root = exeDirectory.Split(srcbin)[0];
            Console.WriteLine(root);

            if (root == exeDirectory)
                throw new InvalidOperationException("Couldn't find root of working directory");

            return root;
        }
    }
}
