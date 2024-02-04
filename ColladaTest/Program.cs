using System;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using IONET;

namespace ColladaTest
{
    class Program
    {


        static void Main(string[] args)
        {

            var scene = IOManager.LoadScene("360.dae", new ImportSettings()
            {

            });
            Console.WriteLine(scene);
         /*   var sceneCube = IOManager.LoadScene("cube.glb", new ImportSettings()
            {

            });
            IOManager.ExportScene(scene, "untitledRB.glb", new ExportSettings()
            {

            });*/

            //  Console.WriteLine("Mesh Count: " + scene.Models[0].Meshes.Count);
        }
    }
}