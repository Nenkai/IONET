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
using SharpGLTF.Materials;

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
                //todo set texture maps
                var material = new MaterialBuilder(iomaterial.Name);
                if (iomaterial.DiffuseMap != null)
                {
                    material.WithChannelImage(KnownChannel.BaseColor, iomaterial.DiffuseMap.FilePath);
                }
                if (iomaterial.NormalMap != null)
                {
                    material.WithChannelImage(KnownChannel.Normal, iomaterial.NormalMap.FilePath);
                }
                var mat = modelRoot.CreateMaterial(material);
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

            Skin? skin = null;
            if (Joints.Count > 0)
            {
                skin = modelRoot.CreateSkin($"Armature");
                skin.Skeleton = Joints[0];
            }

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
                bool hasJoints0 = false;
                bool hasJoints1 = false;

                foreach (var iopoly in iomesh.Polygons)
                {
                    var prim = node.Mesh.CreatePrimitive();

                    SetVertexData(prim, "POSITION", iomesh.Vertices.Select(x => x.Position).ToList());

                    if (iomesh.HasNormals)
                        SetVertexData(prim, "NORMAL", iomesh.Vertices.Select(x => x.Normal).ToList());

                    if (iomesh.HasTangents)
                        SetVertexData(prim, "TANGENT", iomesh.Vertices.Select(X => new Vector4(X.Tangent, 1f)).ToList());

                    //uv set
                    for (int i = 0; i < 8; i++)
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
                    Vector4[] boneIndices0 = new Vector4[iomesh.Vertices.Count];
                    Vector4[] boneWeights0 = new Vector4[iomesh.Vertices.Count];
                    Vector4[] boneIndices1 = new Vector4[iomesh.Vertices.Count];
                    Vector4[] boneWeights1 = new Vector4[iomesh.Vertices.Count];

                    for (int i = 0; i < iomesh.Vertices.Count; i++)
                    {
                        List<float> weights = new List<float>(8);
                        List<int> indices = new List<int>(8);

                        var vertex = iomesh.Vertices[i];
                        if (node.Skin != null)
                        {
                            for (int j = 0; j < vertex.Envelope.Weights.Count; j++)
                            {
                                indices.Add(GetBoneIndex(node.Skin, vertex.Envelope.Weights[j].BoneName));
                                weights.Add(vertex.Envelope.Weights[j].Weight);
                            }
                        }

                        if (indices.Count > 0)
                        {
                            boneIndices0[i] = new Vector4(indices.Count >= 1 ? indices[0] : 0,
                                indices.Count >= 2 ? indices[1] : 0,
                                indices.Count >= 3 ? indices[2] : 0,
                                indices.Count >= 4 ? indices[3] : 0);

                            boneWeights0[i] = new Vector4(weights.Count >= 1 ? weights[0] : 0,
                                weights.Count >= 2 ? weights[1] : 0,
                                weights.Count >= 3 ? weights[2] : 0,
                                weights.Count >= 4 ? weights[3] : 0);
                            hasJoints0 = true;

                            if (indices.Count >= 5)
                            {
                                boneIndices1[i] = new Vector4(indices.Count >= 5 ? indices[4] : 0,
                                    indices.Count >= 6 ? indices[5] : 0,
                                    indices.Count >= 7 ? indices[6] : 0,
                                    indices.Count >= 8 ? indices[7] : 0);

                                boneWeights1[i] = new Vector4(weights.Count >= 5 ? weights[4] : 0,
                                    weights.Count >= 6 ? weights[5] : 0,
                                    weights.Count >= 7 ? weights[6] : 0,
                                    weights.Count >= 8 ? weights[7] : 0);
                                hasJoints1 = true;
                            }
                        }
                    }

                    if (hasJoints0)
                    {
                        SetVertexData(prim, "WEIGHTS_0", boneWeights0.ToList());
                        SetVertexDataBoneIndices(prim, "JOINTS_0", boneIndices0.ToList());
                    }

                    if (hasJoints1)
                    {
                        SetVertexData(prim, "WEIGHTS_1", boneWeights1.ToList());
                        SetVertexDataBoneIndices(prim, "JOINTS_1", boneIndices1.ToList());
                    }

                    //Indices
                    SetIndexData(prim, iopoly.Indicies);

                    //Material
                    prim.Material = modelRoot.LogicalMaterials.FirstOrDefault(
                        x => x.Name == iopoly.MaterialName);
                }
            }

            //bind joints last after all the mesh data is set
            if (Joints.Count > 0)
                skin?.BindJoints(Joints.ToArray());

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

                // Collect all unique times across all tracks for this bone
                var allTimes = new SortedSet<float>();

                // Temporary storage for individual components
                Dictionary<float, float> posX = new Dictionary<float, float>();
                Dictionary<float, float> posY = new Dictionary<float, float>();
                Dictionary<float, float> posZ = new Dictionary<float, float>();

                Dictionary<float, float> quatX = new Dictionary<float, float>();
                Dictionary<float, float> quatY = new Dictionary<float, float>();
                Dictionary<float, float> quatZ = new Dictionary<float, float>();
                Dictionary<float, float> quatW = new Dictionary<float, float>();

                Dictionary<float, float> scaleX = new Dictionary<float, float>();
                Dictionary<float, float> scaleY = new Dictionary<float, float>();
                Dictionary<float, float> scaleZ = new Dictionary<float, float>();

                // Collect keyframe data by track type
                foreach (var track in group.Tracks)
                {
                    foreach (var keyFrame in track.KeyFrames)
                    {
                        float time = keyFrame.Frame / 30.0f; // Convert frame to seconds (assuming 30 fps)
                        allTimes.Add(time);

                        switch (track.ChannelType)
                        {
                            case IOAnimationTrackType.PositionX:
                                posX[time] = keyFrame.ValueF32;
                                break;
                            case IOAnimationTrackType.PositionY:
                                posY[time] = keyFrame.ValueF32;
                                break;
                            case IOAnimationTrackType.PositionZ:
                                posZ[time] = keyFrame.ValueF32;
                                break;

                            case IOAnimationTrackType.QuatX:
                                quatX[time] = keyFrame.ValueF32;
                                break;
                            case IOAnimationTrackType.QuatY:
                                quatY[time] = keyFrame.ValueF32;
                                break;
                            case IOAnimationTrackType.QuatZ:
                                quatZ[time] = keyFrame.ValueF32;
                                break;
                            case IOAnimationTrackType.QuatW:
                                quatW[time] = keyFrame.ValueF32;
                                break;

                            case IOAnimationTrackType.ScaleX:
                                scaleX[time] = keyFrame.ValueF32;
                                break;
                            case IOAnimationTrackType.ScaleY:
                                scaleY[time] = keyFrame.ValueF32;
                                break;
                            case IOAnimationTrackType.ScaleZ:
                                scaleZ[time] = keyFrame.ValueF32;
                                break;
                        }
                    }
                }

                // Build complete transform data for each unique time
                Dictionary<float, Vector3> translation = new Dictionary<float, Vector3>();
                Dictionary<float, Quaternion> rotation = new Dictionary<float, Quaternion>();
                Dictionary<float, Vector3> scale = new Dictionary<float, Vector3>();

                bool hasPosition = posX.Count > 0 || posY.Count > 0 || posZ.Count > 0;
                bool hasQuaternion = quatX.Count > 0 || quatY.Count > 0 || quatZ.Count > 0 || quatW.Count > 0;
                bool hasScale = scaleX.Count > 0 || scaleY.Count > 0 || scaleZ.Count > 0;

                // Build position data for all times that have position keys
                if (hasPosition)
                {
                    foreach (var time in allTimes)
                    {
                        if (posX.ContainsKey(time) || posY.ContainsKey(time) || posZ.ContainsKey(time))
                        {
                            translation[time] = new Vector3(
                                posX.ContainsKey(time) ? posX[time] : 0,
                                posY.ContainsKey(time) ? posY[time] : 0,
                                posZ.ContainsKey(time) ? posZ[time] : 0
                            );
                        }
                    }
                }

                // Build rotation data directly from quaternion
                if (hasQuaternion)
                {
                    foreach (var time in allTimes)
                    {
                        if (quatX.ContainsKey(time) || quatY.ContainsKey(time) ||
                            quatZ.ContainsKey(time) || quatW.ContainsKey(time))
                        {
                            rotation[time] = new Quaternion(
                                quatX.ContainsKey(time) ? quatX[time] : 0,
                                quatY.ContainsKey(time) ? quatY[time] : 0,
                                quatZ.ContainsKey(time) ? quatZ[time] : 0,
                                quatW.ContainsKey(time) ? quatW[time] : 1
                            );
                        }
                    }
                }

                // Build scale data
                if (hasScale)
                {
                    foreach (var time in allTimes)
                    {
                        if (scaleX.ContainsKey(time) || scaleY.ContainsKey(time) || scaleZ.ContainsKey(time))
                        {
                            scale[time] = new Vector3(
                                scaleX.ContainsKey(time) ? scaleX[time] : 1,
                                scaleY.ContainsKey(time) ? scaleY[time] : 1,
                                scaleZ.ContainsKey(time) ? scaleZ[time] : 1
                            );
                        }
                    }
                }

                // Create animation channels only if there's data
                if (translation.Count > 0)
                    anim.CreateTranslationChannel(node, translation);
                if (rotation.Count > 0)
                    anim.CreateRotationChannel(node, rotation);
                if (scale.Count > 0)
                    anim.CreateScaleChannel(node, scale);
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
