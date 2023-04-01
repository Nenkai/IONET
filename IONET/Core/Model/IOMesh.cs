using IONET.Core.IOMath;
using IONET.Core.Skeleton;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace IONET.Core.Model
{
    /// <summary>
    /// 
    /// </summary>
    public class IOMesh
    {
        /// <summary>
        /// 
        /// </summary>
        public string Name { get; set; } = "Mesh";

        /// <summary>
        /// 
        /// </summary>
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

        /// <summary>
        /// 
        /// </summary>        
        public List<IOVertex> Vertices = new List<IOVertex>();

        /// <summary>
        /// 
        /// </summary>
        public IOBone ParentBone;

        /// <summary>
        /// 
        /// </summary>        
        public List<IOPolygon> Polygons = new List<IOPolygon>();
        
        public bool HasNormals { get; set; } = true;
        public bool HasTangents { get; set; } = false;
        public bool HasBitangents { get; set; } = false;

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool HasEnvelopes()
        {
            return Vertices.Exists(e=>e.Envelope.Weights.Count > 0);
        }

        /// <summary>
        ///  Returns true if vertices contains given color set
        /// </summary>
        /// <param name="set"></param>
        /// <returns></returns>
        public bool HasColorSet(int set)
        {
            if (Vertices.Count == 0)
                return false;

            return set < Vertices[0].Colors.Count;
        }

        /// <summary>
        /// Returns true if vertices contains given uv set
        /// </summary>
        /// <param name="set"></param>
        /// <returns></returns>
        public bool HasUVSet(int set)
        {
            if (Vertices.Count == 0)
                return false;

            return set < Vertices[0].UVs.Count;
        }

        /// <summary>
        /// Transforms all vertices by given matrix
        /// </summary>
        /// <param name="mat"></param>
        public void TransformVertices(Matrix4x4 mat)
        {
            foreach(var v in Vertices)
                v.Transform(mat);
        }

        /// <summary>
        /// Converts all polygons except line and point primtives to triangles
        /// </summary>
        public void MakeTriangles()
        {
            foreach (var p in Polygons)
                p.ToTriangles(this);

            // merge by material into one triangle list per material
            Dictionary<string, IOPolygon> materialToPolygon = new Dictionary<string, IOPolygon>();

            // store non triangle-based polygons
            List<IOPolygon> nonTri = new List<IOPolygon>();

            foreach(var p in Polygons)
            {
                // non triangle-based polygons get added regardless
                if(p.PrimitiveType == IOPrimitive.LINE || p.PrimitiveType == IOPrimitive.POINT || p.PrimitiveType == IOPrimitive.LINESTRIP)
                {
                    nonTri.Add(p);
                    continue;
                }

                if (p.MaterialName == null)
                    p.MaterialName = "No_Material";
                
                // add indices based on material
                if (!materialToPolygon.ContainsKey(p.MaterialName))
                    materialToPolygon.Add(p.MaterialName, p);
                else
                    materialToPolygon[p.MaterialName].Indicies.AddRange(p.Indicies);
            }

            // add newly sorted polygons
            Polygons.Clear();
            Polygons.AddRange(materialToPolygon.Values);
            Polygons.AddRange(nonTri);
        }

        /// <summary>
        /// Optimizes geometry by removing redundant vertices
        /// </summary>
        public void Optimize()
        {
            Dictionary<IOVertex, int> remapVertex = new Dictionary<IOVertex, int>();
            Dictionary<int, int> remapIndex = new Dictionary<int, int>();
            List<IOVertex> newVertices = new List<IOVertex>();
            
            // create and remap vertices
            int vi = 0;
            foreach (var v in Vertices)
            {
                if (!remapVertex.ContainsKey(v))
                {
                    remapVertex.Add(v, newVertices.Count);
                    newVertices.Add(v);
                }
                remapIndex.Add(vi, remapVertex[v]);
                vi++;
            }

            // remap polygon indices
            foreach(var p in Polygons)
                for(int i = 0; i < p.Indicies.Count; i++)
                    p.Indicies[i] = remapIndex[p.Indicies[i]];

            System.Diagnostics.Debug.WriteLine("Optimized: " + Vertices.Count + " -> " + newVertices.Count);

            Vertices = newVertices;
        }

        public List<IOMesh> SplitByMaterial()
        {
            if (Polygons.Count == 0)
                return new List<IOMesh>();

            List<IOMesh> meshes = new List<IOMesh>();

            // remap polygon indices
            for (int j = 0; j < Polygons.Count; j++)
            {
                //Keep existing base mesh. Split polygon groups if materials are unique
                if (j == 0)
                    continue;

                var p = Polygons[j];

                IOMesh mesh = new IOMesh();
                mesh.Name = $"{Name}_{j}";
                meshes.Add(mesh);

                IOPolygon poly = new IOPolygon();
                poly.Indicies = new List<int>();
                poly.Attribute = p.Attribute;
                poly.MaterialName = p.MaterialName;
                poly.PrimitiveType = p.PrimitiveType;
                mesh.Polygons.Add(poly);

                Dictionary<IOVertex, int> remapVertex = new Dictionary<IOVertex, int>();
                for (int i = 0; i < p.Indicies.Count; i++)
                {
                    var v = Vertices[p.Indicies[i]];
                    if (!remapVertex.ContainsKey(v))
                    {
                        remapVertex.Add(v, mesh.Vertices.Count);
                        mesh.Vertices.Add(v);
                    }
                    poly.Indicies.Add(remapVertex[v]);
                }
                remapVertex.Clear();
            }

            //Clear out all but the first polygons and vertices
            var polyF = Polygons.FirstOrDefault();
            Polygons.Clear();
            Polygons.Add(polyF);

            Optimize();

            return meshes;
        }

        /// <summary>
        /// Generates Tangents and Bitangents for the vertices
        /// </summary>
        public void GenerateTangentsAndBitangents()
        {
            List<int> indices = new List<int>();

            foreach(var v in Polygons)
            {
                v.ToTriangles(this);

                if (v.PrimitiveType != IOPrimitive.TRIANGLE)
                    continue;

                indices.AddRange(v.Indicies);
            }

            var positions = Vertices.Select(e => e.Position).ToList();
            var normals = Vertices.Select(e => e.Normal).ToList();
            var uvs = HasUVSet(0) ? Vertices.Select(e => e.UVs[0]).ToList(): Vertices.Select(e => Vector2.Zero).ToList();

            TriangleListUtils.CalculateTangentsBitangents(positions, normals, uvs, indices, out Vector3[] tangents, out Vector3[] bitangents);

            for (int i = 0; i < Vertices.Count; i++)
            {
                var vertex = Vertices[i];
                vertex.Tangent = tangents[i];
                vertex.Binormal = bitangents[i];
                Vertices[i] = vertex;
            }
        }
    }
}
