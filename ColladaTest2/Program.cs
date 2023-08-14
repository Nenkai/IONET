using System;
using IONET;

namespace ColladaTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var scene = IOManager.LoadScene("untitled.dae", new ImportSettings()
            {

            });
            Console.WriteLine("Mesh Count: " + scene.Models[0].Meshes.Count);
        }
    }
}
