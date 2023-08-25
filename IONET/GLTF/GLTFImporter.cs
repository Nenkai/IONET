using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using IONET.Collada.Core.Scene;
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
using IONET.Collada.Core.Animation;

namespace IONET.GLTF
{
    public class GLTFImporter : ISceneLoader
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public IOScene GetScene(string filePath)
        {
            // generate a new scene
            IOScene scene = new IOScene();
            IOModel iomodel = new IOModel();
            iomodel.Name = Path.GetFileNameWithoutExtension(filePath);
            scene.Models.Add(iomodel);

            var model = SharpGLTF.Schema2.ModelRoot.Load(filePath);
            foreach (var node in model.LogicalScenes[0].VisualChildren)
                ProcessNodes(iomodel, node, null, Matrix4x4.Identity);

            //Animation handling
            foreach (var anim in model.LogicalAnimations)
            {
                foreach (var channel in anim.Channels)
                {
                    if (channel.TargetNode == null)
                        continue;

                    var trans = channel.GetTranslationSampler();
                    var rotation = channel.GetRotationSampler(); //in quat
                    var scale = channel.GetScaleSampler();
                    var name = channel.TargetNode.Name;
                }
            }

            foreach (var mat in model.LogicalMaterials)
            {
                IOMaterial iomaterial = new IOMaterial();
                iomaterial.Name = mat.Name;
                scene.Materials.Add(iomaterial);

                //Texture map handling
                foreach (var channel in mat.Channels)
                {
                    //No texture, skip
                    if (channel.Texture == null || channel.Texture.PrimaryImage == null)
                        continue;

                    //texture name is empty, skip
                    string texName = channel.Texture.PrimaryImage.Name;
                    if (string.IsNullOrEmpty(texName))
                        continue;

                    switch (channel.Key)
                    {
                        case "BaseColor": iomaterial.DiffuseMap = ConvertTextureMap(channel.Texture, channel.TextureSampler); break;
                        case "Normal": iomaterial.NormalMap = ConvertTextureMap(channel.Texture, channel.TextureSampler); break;
                        case "Emissive": iomaterial.EmissionMap = ConvertTextureMap(channel.Texture, channel.TextureSampler); break;
                    }
                }
            }


            // done
            return scene;
        }

        private IOTexture ConvertTextureMap(SharpGLTF.Schema2.Texture texture, SharpGLTF.Schema2.TextureSampler sampler)
        {
            WrapMode ConvertWrap(TextureWrapMode mode)
            {
                switch (mode)
                {
                    case TextureWrapMode.REPEAT: return WrapMode.REPEAT;
                    case TextureWrapMode.MIRRORED_REPEAT: return WrapMode.MIRROR;
                    case TextureWrapMode.CLAMP_TO_EDGE: return WrapMode.CLAMP;
                }
                return WrapMode.REPEAT;
            }

            string path = texture.PrimaryImage.AlternateWriteFileName;

            return new IOTexture()
            {
                Name = texture.PrimaryImage.Name,
                FilePath = path == null ? "" : path,
                WrapS = ConvertWrap(texture.Sampler.WrapS),
                WrapT = ConvertWrap(texture.Sampler.WrapT),
            };
        }

        private void ProcessNodes(IOModel iomodel, SharpGLTF.Schema2.Node node, IOBone boneParent, Matrix4x4 parentMatrix)
        {
            var worldTransform = node.LocalMatrix * parentMatrix;

            if (node.Mesh != null)
                iomodel.Meshes.Add(CreateMesh(node.Mesh, node.Skin, node.WorldMatrix));

            //Add bone if skinning type is used or parent bone exists
            IOBone iobone = null;
            if (node.IsSkinSkeleton || node.IsSkinJoint || boneParent != null)
            {
                iobone = new IOBone();
                iobone.Name = node.Name;
                iobone.LocalTransform = node.LocalMatrix;
                if (boneParent != null)
                    boneParent.AddChild(iobone);
                else
                {
                    //apply parent matrix to apply prior node transforms to the skeleton root
                    iobone.LocalTransform = node.LocalMatrix * parentMatrix;

                    iomodel.Skeleton.RootBones.Add(iobone);
                }
            }

            foreach (var child in node.VisualChildren)
                ProcessNodes(iomodel, child, iobone, worldTransform);
        }

        private IOMesh CreateMesh(SharpGLTF.Schema2.Mesh mesh, SharpGLTF.Schema2.Skin skin, Matrix4x4 worldTransform)
        {
            IOMesh iomesh = new IOMesh();
            iomesh.Name = mesh.Name;
            foreach (var prim in mesh.Primitives)
            {
                IOPolygon iopoly = new IOPolygon();
                iomesh.Polygons.Add(iopoly);

                if (prim.Material != null)
                    iopoly.MaterialName = prim.Material.Name;

                foreach (var tri in prim.GetTriangleIndices())
                {
                    iopoly.Indicies.Add(tri.A);
                    iopoly.Indicies.Add(tri.B);
                    iopoly.Indicies.Add(tri.C);
                }
                //tex coord channel list
                List<IList<Vector2>> texCoords = new List<IList<Vector2>>();
                foreach (var accessor in prim.VertexAccessors)
                {
                    if (accessor.Key.StartsWith("TEXCOORD_"))
                        texCoords.Add(accessor.Value.AsVector2Array());
                }
                //color channel list
                List<IList<Vector4>> colorList = new List<IList<Vector4>>();
                foreach (var accessor in prim.VertexAccessors)
                {
                    if (accessor.Key.StartsWith("COLOR_"))
                        colorList.Add(accessor.Value.AsColorArray());
                }
                //weight list
                List<IList<Vector4>> weightList = new List<IList<Vector4>>();
                foreach (var accessor in prim.VertexAccessors)
                {
                    if (accessor.Key.StartsWith("WEIGHTS_"))
                        weightList.Add(accessor.Value.AsVector4Array());
                }
                //bone index list
                List<IList<Vector4>> boneIndexList = new List<IList<Vector4>>();
                foreach (var accessor in prim.VertexAccessors)
                {
                    if (accessor.Key.StartsWith("JOINTS_"))
                        boneIndexList.Add(accessor.Value.AsVector4Array());
                }
                //positions
                var pos = prim.GetVertexAccessor("POSITION").AsVector3Array();
                //normals
                var nrm = prim.GetVertexAccessor("NORMAL")?.AsVector3Array();
                //tangents
                var tangent = prim.GetVertexAccessor("TANGENT")?.AsVector4Array();

                //Init a vertex list
                IOVertex[] vertices = new IOVertex[pos.Count];
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = new IOVertex();
                    //positions
                    vertices[i].Position = pos[i];
                    //normals
                    if (nrm?.Count > 0) vertices[i].Normal = nrm[i];
                    //tex coord channel list
                    for (int j = 0; j < texCoords?.Count; j++)
                        vertices[i].SetUV(texCoords[j][i].X, texCoords[j][i].Y, j);
                    //vertex color channel list
                    for (int j = 0; j < colorList?.Count; j++)
                        vertices[i].SetColor(
                            colorList[j][i].X, colorList[j][i].Y, colorList[j][i].Z, colorList[j][i].W, j);
                    //tangents
                    if (tangent?.Count > 0) 
                        vertices[i].Tangent = new Vector3(tangent[i].X, tangent[i].Y, tangent[i].Z);

                    if (weightList.Count > 0) //bone indices + weights
                    {
                        for (int b = 0; b < boneIndexList.Count; b++)
                        {
                            var bones4 = boneIndexList[b][i];
                            var weights4 = weightList[b][i];

                            float[] weights = new float[4] { weights4.X, weights4.Y, weights4.Z, weights4.W };
                            float[] indices = new float[4] { bones4.X, bones4.Y, bones4.Z, bones4.W };

                            for (int j = 0; j < 4; j++)
                            {
                                if (weights[j] == 0)
                                    continue;

                                var joint = skin.GetJoint((int)indices[j]);
                                vertices[i].Envelope.Weights.Add(new IOBoneWeight()
                                {
                                    BoneName = joint.Joint.Name,
                                    Weight = weights[j],
                                });
                            }
                        }
                    }
                    else if (boneIndexList.Count > 0) //bone indices rigid, no weights
                    {
                        var bones = boneIndexList[0][i];
                        var joint = skin.GetJoint((int)bones.X);
                        //only 1 bone
                        vertices[i].Envelope.Weights.Add(new IOBoneWeight()
                        {
                            BoneName = joint.Joint.Name,
                            Weight = 1f,
                        });
                    }
                }
                iomesh.Vertices.AddRange(vertices);

                //transform mesh
            //    iomesh.TransformVertices(worldTransform);
            }
            return iomesh;
        }
    }
}
