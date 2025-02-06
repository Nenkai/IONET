using IONET.Core.Model;
using IONET.Core;
using IONET.Fbx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Assimp;
using IONET.Core.Skeleton;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace IONET.Assimp
{
    internal class AssimpImport : ISceneLoader
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public IOScene GetScene(string filePath)
        {
            PostProcessSteps postProcess = PostProcessSteps.Triangulate;
            postProcess |= PostProcessSteps.OptimizeMeshes;
            postProcess |= PostProcessSteps.CalculateTangentSpace;

            IOScene ioscene = new IOScene();
            IOModel iomodel = new IOModel();
            ioscene.Models.Add(iomodel);

            using AssimpContext assimpContext = new AssimpContext();

            var scene = assimpContext.ImportFile(filePath, postProcess);
            scene.Metadata.Clear();

            LoadMaterials(ioscene, scene);
            LoadNodes(ioscene, iomodel, scene);

            ioscene.LoadSkeletonFromNodes(iomodel);

            return ioscene;
        }

        private void LoadMaterials(IOScene ioscene, Scene scene)
        {
            WrapMode ConvertWrap(TextureWrapMode mode)
            {
                switch (mode)
                {
                    case TextureWrapMode.Wrap: return WrapMode.REPEAT;
                    case TextureWrapMode.Mirror: return WrapMode.MIRROR;
                    case TextureWrapMode.Clamp: return WrapMode.CLAMP;
                }
                return WrapMode.REPEAT;
            }

            IOTexture ConvertTexture(TextureSlot slot, bool hasTexture)
            {
                if (!hasTexture) return null;

                return new IOTexture()
                {
                    FilePath = slot.FilePath,
                    UVChannel = slot.UVIndex,
                    WrapS = ConvertWrap(slot.WrapModeU),
                    WrapT = ConvertWrap(slot.WrapModeV),
                    Name = Path.GetFileNameWithoutExtension(slot.FilePath),
                };
            }

            foreach (var material in scene.Materials)
            {
                ioscene.Materials.Add(new IOMaterial()
                {
                    Name = material.Name,   
                    DiffuseMap = ConvertTexture(material.TextureDiffuse, material.HasTextureDiffuse),
                    NormalMap = ConvertTexture(material.TextureNormal, material.HasTextureNormal),
                    EmissionMap = ConvertTexture(material.TextureEmissive, material.HasTextureEmissive),
                    AmbientMap = ConvertTexture(material.TextureAmbient, material.HasTextureAmbient),
                    AmbientOcclusionMap = ConvertTexture(material.TextureAmbientOcclusion, material.HasTextureAmbientOcclusion),
                    ReflectiveMap = ConvertTexture(material.TextureReflection, material.HasTextureReflection),
                    SpecularMap = ConvertTexture(material.TextureSpecular, material.HasTextureSpecular),
                    DiffuseColor = material.ColorDiffuse,
                    SpecularColor = material.ColorSpecular,
                    EmissionColor = material.ColorEmissive,
                    Alpha = material.TransparencyFactor,
                    Shininess = material.Shininess,
                });
            }
        }

        private void LoadNodes(IOScene ioscene, IOModel iomodel, Scene assimpScene)
        {
            foreach (var child in assimpScene.RootNode.Children)
                ioscene.Nodes.Add(ConvertNode(iomodel, assimpScene, child, Matrix4x4.Identity)); 
        }

        private IONode ConvertNode(IOModel iomodel, Scene assimpScene, Node assimpNode, Matrix4x4 parentTransform)
        {
            var ionode = new IONode();

            ionode.Name = assimpNode.Name;
            ionode.LocalTransform = assimpNode.Transform.ToNumerics();
            // Joint if node has meshes with no children attached
            ionode.IsJoint = !(assimpNode.HasMeshes && assimpNode.ChildCount == 0);

             var worldTansform = ionode.LocalTransform * parentTransform;

            if (assimpNode.HasMeshes)
            {
                for (int i = 0; i < assimpNode.MeshCount; i++)
                    iomodel.Meshes.Add(LoadMesh(assimpScene,
                        assimpScene.Meshes[assimpNode.MeshIndices[i]], worldTansform));

                ionode.Mesh = iomodel.Meshes[0];
            }

            foreach (var node in assimpNode.Children)
                ionode.AddChild(ConvertNode(iomodel, assimpScene, node, worldTansform));

            return ionode;
        }

        private IOMesh LoadMesh(Scene scene, Mesh assimpMesh, Matrix4x4 worldTansform)
        {
            IOMesh iomesh = new IOMesh()
            {
                Name = assimpMesh.Name,
            };

            IOEnvelope[] envelopes = new IOEnvelope[assimpMesh.VertexCount];
            for (int i = 0; i < assimpMesh.VertexCount; i++)
                envelopes[i] = new IOEnvelope();

            for (int j = 0; j < assimpMesh.BoneCount; j++)
            {
                var bone = assimpMesh.Bones[j];
                foreach (var w in bone.VertexWeights)
                {
                    envelopes[w.VertexID].Weights.Add(new IOBoneWeight()
                    {
                        BoneName = bone.Name,
                        Weight = w.Weight, 
                    });
                }
            }

            Console.WriteLine($"{iomesh.Name} {assimpMesh.BoneCount}");

            for (int i = 0; i < assimpMesh.VertexCount; i++)
            {
                IOVertex iovertex = new IOVertex()
                {
                    Position = assimpMesh.Vertices[i],
                };
                if (assimpMesh.HasNormals)
                    iovertex.Normal = assimpMesh.Normals[i];
                if (assimpMesh.HasTangentBasis)
                {
                    iovertex.Tangent = assimpMesh.Tangents[i];
                    iovertex.Binormal = assimpMesh.BiTangents[i];
                }

                for (int u = 0; u < assimpMesh.TextureCoordinateChannelCount; u++)
                {
                    if (assimpMesh.HasTextureCoords(u))
                        iovertex.SetUV(
                             assimpMesh.TextureCoordinateChannels[u][i].X,
                             assimpMesh.TextureCoordinateChannels[u][i].Y, u);
                }
                for (int c = 0; c < assimpMesh.VertexColorChannelCount; c++)
                {
                    if (assimpMesh.HasVertexColors(c))
                        iovertex.SetColor(
                             assimpMesh.VertexColorChannels[c][i].X,
                             assimpMesh.VertexColorChannels[c][i].Y,
                             assimpMesh.VertexColorChannels[c][i].Z,
                             assimpMesh.VertexColorChannels[c][i].W, c);
                }

                foreach (var weight in envelopes[i].Weights)
                    iovertex.Envelope.Weights.Add(weight);

                iomesh.Vertices.Add(iovertex);
            }

            IOPolygon iopolygon = new IOPolygon();
            iomesh.Polygons.Add(iopolygon);

            for (int i = 0; i < assimpMesh.FaceCount; i++)
            {
                // Trangle
                iopolygon.Indicies.Add(assimpMesh.Faces[i].Indices[0]);
                iopolygon.Indicies.Add(assimpMesh.Faces[i].Indices[1]);
                iopolygon.Indicies.Add(assimpMesh.Faces[i].Indices[2]);
            }

            if (assimpMesh.MaterialIndex != -1 && assimpMesh.MaterialIndex < scene.MaterialCount)
                iopolygon.MaterialName = scene.Materials[assimpMesh.MaterialIndex].Name;

            iomesh.TransformVertices(worldTansform);

            return iomesh;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string[] GetExtensions()
        {
            return new string[] { ".fbx" };
        }

        public string[] GetSupportedImportFormats()
        {
            using AssimpContext assimpContext = new AssimpContext();
            return assimpContext.GetSupportedImportFormats();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string Name()
        {
            return "Autodesk FBX";
        }

        public bool Verify(string filePath)
        {
            return Path.GetExtension(filePath).ToLower().Equals(".fbx") && AssimpHelper.IsRuntimePresent();
        }
    }
}
