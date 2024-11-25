using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using System.Numerics;
using IONET.Collada.Helpers;
using IONET.Collada.Core.Geometry;
using IONET.Collada.Enums;
using IONET.Collada.FX.Materials;
using IONET.Collada.FX.Rendering;
using IONET.Collada.FX.Profiles.COMMON;
using System.Xml;
using SharpGLTF.Schema2;
using SharpGLTF.Memory;
using IONET.Collada.B_Rep.Surfaces;
using IONET.Core.Animation;

namespace IONET.GLTF
{
    public class GLTFExporter : ISceneExporter
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string Name()
        {
            return "GLTF";
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string[] GetExtensions()
        {
            return new string[] { ".gltf", ".glb" };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool Verify(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return ext.Equals(".gltf") || ext.Equals(".glb");
        }

        public void ExportScene(IOScene ioscene, string filePath, ExportSettings settings)
        {
            var modelRoot = ModelRoot.CreateModel();
            var iomodel = ioscene.Models.FirstOrDefault();
            var sceneRoot = modelRoot.UseScene("Scene").CreateNode("Armature");

            foreach (var iomaterial in ioscene.Materials)
            {
                var mat = modelRoot.CreateMaterial(iomaterial.Name);
                //todo set texture maps
            }

            List<Node> Joints = new List<Node>();

            void CreateBone(IOBone bone, Node parent)
            {
                if (string.IsNullOrEmpty(bone.Name))
                    return;

                Node node = parent.CreateNode(bone.Name);
                node.LocalMatrix = bone.LocalTransform;
                Joints.Add(node);

                foreach (var child in bone.Children)
                    CreateBone(child, node);
            }

            foreach (var child in iomodel.Skeleton.RootBones)
                CreateBone(child, sceneRoot);

            int GetBoneIndex(Skin skin, string name)
            {
                var bone = Joints.FirstOrDefault(x => x.Name == name);
                if (bone != null)
                    return Joints.IndexOf(bone);
                return 0;
            }

            var skin = modelRoot.CreateSkin($"Armature");
            skin.Skeleton = Joints[0];

            foreach (var ioanim in ioscene.Animations)
            {
                SetAnimationData(modelRoot, ioanim, Joints);
            }

            foreach (var iomesh in iomodel.Meshes)
            {
                if (string.IsNullOrEmpty(iomesh.Name))
                    continue;

                var poly = iomesh.Polygons.FirstOrDefault();
                
                //Create node
                Node node = sceneRoot.CreateNode($"{iomesh.Name}");
                //Create mesh
                node.Mesh = modelRoot.CreateMesh($"{iomesh.Name}");
                node.LocalMatrix = Matrix4x4.Identity;
                //Rigging
                if (iomesh.HasEnvelopes() && Joints.Count > 0)
                    node.Skin = skin;

                //Todo vertex list should be created by polygon indices and re indexed
                foreach (var iopoly in iomesh.Polygons)
                {
                    var prim = node.Mesh.CreatePrimitive();
                    SetVertexData(prim, "POSITION", iomesh.Vertices.Select(x => x.Position).ToList());
                    SetVertexData(prim, "NORMAL", iomesh.Vertices.Select(x => x.Normal).ToList());
                    //uv set
                    for (int i = 0; i  < 8; i++)
                    {
                        if (iomesh.HasUVSet(i))
                            SetVertexData(prim, $"TEXCOORD_{i}", iomesh.Vertices.Select(x => x.UVs[i]).ToList());
                    }
                    //color set
                    for (int i = 0; i < 4; i++)
                    {
                        if (iomesh.HasColorSet(i))
                            SetVertexData(prim, $"COLOR_{i}", iomesh.Vertices.Select(x => x.Colors[i]).ToList());
                    }

                    //Bones and weights
                    Vector4[] boneIndices = new Vector4[iomesh.Vertices.Count];
                    Vector4[] boneWeights = new Vector4[iomesh.Vertices.Count];

                    bool hasSecondSet = iomesh.Vertices.Any(x => x.Envelope.Weights.Count > 4);

                    Vector4[] boneIndicesSet2 = new Vector4[hasSecondSet ? iomesh.Vertices.Count : 0];
                    Vector4[] boneWeightsSet2 = new Vector4[hasSecondSet ? iomesh.Vertices.Count : 0];

                    for (int i = 0; i < iomesh.Vertices.Count; i++)
                    {
                        float[] weights = new float[hasSecondSet ? 8 : 4];
                        int[] indices = new int[hasSecondSet ? 8 : 4];

                        var vertex = iomesh.Vertices[i];
                        for (int j = 0; j < vertex.Envelope.Weights.Count; j++)
                        {
                            weights[j] = vertex.Envelope.Weights[j].Weight;
                            indices[j] = GetBoneIndex(node.Skin, vertex.Envelope.Weights[j].BoneName);
                        }

                        boneWeights[i] = new Vector4(weights[0], weights[1], weights[2], weights[3]);
                        boneIndices[i] = new Vector4(indices[0], indices[1], indices[2], indices[3]);

                        if (hasSecondSet)
                        {
                            boneWeightsSet2[i] = new Vector4(weights[4], weights[5], weights[6], weights[7]);
                            boneIndicesSet2[i] = new Vector4(indices[4], indices[5], indices[6], indices[7]);
                        }
                    }

                    SetVertexData(prim, "WEIGHTS_0", boneWeights.ToList());
                    SetVertexDataBoneIndices(prim, "JOINTS_0", boneIndices.ToList());

                    if (hasSecondSet)
                    {
                        SetVertexData(prim, "WEIGHTS_1", boneWeightsSet2.ToList());
                        SetVertexDataBoneIndices(prim, "JOINTS_1", boneIndicesSet2.ToList());
                    }


                    //Indices
                    SetIndexData(prim, iopoly.Indicies);

                    //Material
                    prim.Material = modelRoot.LogicalMaterials.FirstOrDefault(
                        x => x.Name == iopoly.MaterialName);
                }
            }

            //bind joints last after all the mesh data is set
            skin.BindJoints(Joints.ToArray());

            //Preview nodes
            void ViewNode(Node node, string level)
            {
                Console.WriteLine($"{level} {node.Name}");

                foreach (var child in node.VisualChildren)
                    ViewNode(child, level + "-");
            }

            ViewNode(modelRoot.LogicalScenes[0].VisualChildren.FirstOrDefault(), "");

            modelRoot.SaveGLTF(filePath, new WriteSettings()
            {
                JsonIndented = true,
            });
        }

        private void SetAnimationData(ModelRoot modelRoot, IOAnimation ioanim, List<Node> nodes)
        {
            var anim = modelRoot.CreateAnimation(ioanim.Name);

            foreach (var group in ioanim.Groups)
            {
                var node = nodes.FirstOrDefault(x => x.Name == group.Name);
                if (node == null)
                    continue;

                Dictionary<float, Vector3> translation = new Dictionary<float, Vector3>();
                Dictionary<float, Quaternion> rotation = new Dictionary<float, Quaternion>();
                Dictionary<float, Vector3> scale = new Dictionary<float, Vector3>();


                foreach (var track in group.Tracks)
                {
                    switch (track.KeyFrames.Count)
                    {

                    }
                }

                if (translation.Count > 0)
                    anim.CreateTranslationChannel(node, translation);
                if (rotation.Count > 0)
                    anim.CreateRotationChannel(node, rotation);
                if (scale.Count > 0)
                    anim.CreateScaleChannel(node, translation);
            }

        }

        private void SetIndexData(MeshPrimitive primitive, List<int> indices)
        {
            var root = primitive.LogicalParent.LogicalParent;

            // create an index buffer and fill it
            var view = root.CreateBufferView(4 * indices.Count, 0, BufferMode.ELEMENT_ARRAY_BUFFER);
            var array = new IntegerArray(view.Content);
            array.Fill(indices);

            var accessor = root.CreateAccessor();
            accessor.SetIndexData(view, 0, indices.Count, IndexEncodingType.UNSIGNED_INT);

            primitive.DrawPrimitiveType = PrimitiveType.TRIANGLES;
            primitive.SetIndexAccessor(accessor);
            primitive.IndexAccessor = accessor;
        }

        private void SetVertexData(MeshPrimitive primitive, string attribute, List<Vector3> vecs)
        {
            var root = primitive.LogicalParent.LogicalParent;

            // create a vertex buffer and fill it
            var view = root.CreateBufferView(12 * vecs.Count, 0, BufferMode.ARRAY_BUFFER);
            var array = new Vector3Array(view.Content);
            array.Fill(vecs);

            var accessor = root.CreateAccessor();
            primitive.SetVertexAccessor(attribute, accessor);

            accessor.SetVertexData(view, 0, vecs.Count, DimensionType.VEC3, EncodingType.FLOAT, false);
        }

        private void SetVertexData(MeshPrimitive primitive, string attribute, List<Vector2> vecs)
        {
            var root = primitive.LogicalParent.LogicalParent;

            // create a vertex buffer and fill it
            var view = root.CreateBufferView(8 * vecs.Count, 0, BufferMode.ARRAY_BUFFER);
            var array = new Vector2Array(view.Content);
            array.Fill(vecs);

            var accessor = root.CreateAccessor();
            primitive.SetVertexAccessor(attribute, accessor);

            accessor.SetVertexData(view, 0, vecs.Count, DimensionType.VEC2, EncodingType.FLOAT, false);
        }

        private void SetVertexData(MeshPrimitive primitive, string attribute, List<Vector4> vecs)
        {
            var root = primitive.LogicalParent.LogicalParent;

            // create a vertex buffer and fill it
            var view = root.CreateBufferView(16 * vecs.Count, 0, BufferMode.ARRAY_BUFFER);
            var array = new Vector4Array(view.Content);
            array.Fill(vecs);

            var accessor = root.CreateAccessor();
            primitive.SetVertexAccessor(attribute, accessor);

            accessor.SetVertexData(view, 0, vecs.Count, DimensionType.VEC4, EncodingType.FLOAT, false);
        }

        private void SetVertexDataBoneIndices(MeshPrimitive primitive, string attribute, List<Vector4> vecs)
        {
            var root = primitive.LogicalParent.LogicalParent;

            // create a vertex buffer and fill it
            var view = root.CreateBufferView(8 * vecs.Count, 0, BufferMode.ARRAY_BUFFER);
            var array = new Vector4Array(view.Content, 0, EncodingType.SHORT);
            array.Fill(vecs);

            var accessor = root.CreateAccessor();
            primitive.SetVertexAccessor(attribute, accessor);
             
            accessor.SetVertexData(view, 0, vecs.Count, DimensionType.VEC4, EncodingType.UNSIGNED_SHORT, false);
        }
    }
}
