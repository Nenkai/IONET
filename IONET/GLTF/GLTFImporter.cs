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

            foreach (var mat in model.LogicalMaterials)
            {
                IOMaterial iomaterial = new IOMaterial();
                iomaterial.Name = mat.Name;
                scene.Materials.Add(iomaterial);

                foreach (var channel in mat.Channels)
                {
                    string texName = "";
                    if (channel.Texture != null)
                        texName = channel.Texture.PrimaryImage.Name;

                    if (string.IsNullOrEmpty(texName))
                        continue;

                    switch (channel.Key)
                    {
                        case "BaseColor":
                            iomaterial.DiffuseMap = new IOTexture()
                            {
                                Name = texName,
                            };
                            break;
                        case "Normal":
                            iomaterial.NormalMap = new IOTexture()
                            {
                                Name = texName,
                            };
                            break;
                        case "Emissive":
                            iomaterial.EmissionMap = new IOTexture()
                            {
                                Name = texName,
                            };
                            break;
                    }
                }
            }


            // done
            return scene;
        }

        private void ProcessNodes(IOModel iomodel, SharpGLTF.Schema2.Node node, IOBone boneParent, Matrix4x4 parentMatrix)
        {
            var worldTransform = node.LocalMatrix * parentMatrix;

            if (node.Mesh != null)
                iomodel.Meshes.Add(CreateMesh(node.Mesh, node.Skin, worldTransform));

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
                    {
                        foreach (var ind in accessor.Value.AsVector4Array())
                            boneIndexList.Add(accessor.Value.AsVector4Array());
                    }
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
                    if (tangent?.Count > 0) vertices[i].Tangent = new Vector3(
                        tangent[i].X, tangent[i].Y, tangent[i].Z);

                    //transform at end
                    vertices[i].Position = Vector3.Transform(vertices[i].Position, worldTransform);
                    vertices[i].Normal = Vector3.TransformNormal(vertices[i].Normal, worldTransform);

                    if (weightList.Count > 0) //bone indices + weights
                    {
                        var bones = boneIndexList[0][i];
                        float[] weights = new float[4] { weightList[0][i].X, weightList[0][i].Y, weightList[0][i].Z, weightList[0][i].W };
                        float[] indices = new float[4] { boneIndexList[0][i].X, boneIndexList[0][i].Y, boneIndexList[0][i].Z, boneIndexList[0][i].W };

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
            }
            return iomesh;
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
    }
}
