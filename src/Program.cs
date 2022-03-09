﻿using System;
using System.IO;

namespace computegen
{
    class Program
    {
        static void Main(string[] args)
        {
            var rootDir = GetRepoRootDirectory();
            var distDir = "dist";

            var rhinocommonPath = Path.Combine("rhino3dm", "src", "dotnet");
            if (!Directory.Exists(rhinocommonPath))
                throw new InvalidOperationException($"RhinoCommon directory not found! ({rhinocommonPath})");

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

            var classes = ClassBuilder.FilteredList(ClassBuilder.AllClasses, filter);

            Console.ForegroundColor = ConsoleColor.Blue;

            /* --- JavaScript --- */

            Console.WriteLine("Writing javascript client");

            string jsDir = Path.Combine(distDir, "javascript");
            Console.WriteLine(jsDir);
            Directory.CreateDirectory(jsDir);

            var js = new JavascriptClient();
            js.Write(ClassBuilder.AllClasses, Path.Combine(jsDir, "compute.rhino3d.js"), filter);
            
            Console.WriteLine("Writing javascript docs");
            RstClient.WriteJavascriptDocs(classes, distDir);

            /* ----- Python ----- */

            Console.WriteLine("Writing python client");

            string pyDir = Path.Combine(distDir, "python");
            Console.WriteLine(pyDir);
            Directory.CreateDirectory(pyDir);

            var py = new PythonClient();
            py.Write(ClassBuilder.AllClasses, pyDir, filter);

            Console.WriteLine("Writing python docs");
            RstClient.WritePythonDocs(classes, distDir);

            /* ------- C# ------- */

            Console.WriteLine("Writing C# client");

            var csDir = Path.Combine(distDir, "dotnet");
            Console.WriteLine(csDir);
            Directory.CreateDirectory(csDir);

            var cs = new DotNetClient();
            cs.Write(ClassBuilder.AllClasses, Path.Combine(csDir, "RhinoCompute.cs"), filter);


            Console.ResetColor();
        }

        static string GetRepoRootDirectory()
        {
            var exeDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            var srcbin = string.Format("{0}src{0}bin{0}", Path.DirectorySeparatorChar);
            var root = exeDirectory.Split(srcbin)[0];

            if (root == exeDirectory)
                throw new InvalidOperationException("Couldn't find root of working directory");

            return root;
        }
    }
}
