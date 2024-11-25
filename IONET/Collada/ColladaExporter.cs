using IONET.Core;
using System;
using System.Collections.Generic;
using IONET.Collada.Core.Scene;
using IONET.Collada.Core.Metadata;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using IONET.Core.Animation;
using IONET.Core.IOMath;
using IONET.Collada.Core.Transform;
using IONET.Collada.Core.Geometry;
using IONET.Collada.Core.Data_Flow;
using IONET.Collada.Core.Animation;
using IONET.Collada.Core.Technique_Common;
using IONET.Collada.Helpers;
using System.Linq;
using IONET.Collada.Enums;
using IONET.Collada.Core.Controller;
using System.Numerics;
using IONET.Collada.FX.Materials;
using IONET.Collada.FX.Effects;
using IONET.Collada.FX.Texturing;
using IONET.Collada.FX.Rendering;
using IONET.Collada.Core.Parameters;
using System.Xml;
using IONET.Collada.Physics.Analytical_Shape;

namespace IONET.Collada
{
    public class ColladaExporter : ISceneExporter
    {
        private ExportSettings _settings;
        private IONET.Collada.Collada _collada;

        private HashSet<string> _usedIDs = new HashSet<string>();

        /// <summary>
        /// Gets a unique ID for given string
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public string GetUniqueID(string id)
        {
            if (!_usedIDs.Contains(id))
            {
                _usedIDs.Add(id);
                return id;
            }
            else
            {
                int index = 0;
                while (_usedIDs.Contains(id + "_" + index))
                    index++;
                _usedIDs.Add(id + "_" + index);
                return id + "_" + index;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string Name()
        {
            return "Collada";
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="filePath"></param>
        public void ExportScene(IOScene scene, string filePath, ExportSettings settings)
        {
            // create collada document
            _collada = new IONET.Collada.Collada();
            _settings = settings;

            _collada.Version = "1.4.1";
            _collada.Asset = new Asset();
            _collada.Asset.Up_Axis = "Y_UP";

            // export materials
            foreach (var mat in scene.Materials)
                ProcessMaterial(mat);
            if (settings.ExportAnimations)
            {
                foreach (IOAnimation animation in scene.Animations)
                    this.ProcessAnimation(animation);
            }

            // initialize scene
            var visscene = new Visual_Scene
            {
                Name = scene.Name
            };
            visscene.ID = scene.Name;


            // export models nodes
            List<Node> nodes = new List<Node>();
            if (scene.Nodes.Count > 0) //custom node tree
            {
                foreach (var mod in scene.Models)
                    nodes.AddRange(ProcessNodeTree(scene, mod));
            }
            else
            {
                foreach (var mod in scene.Models)
                    nodes.AddRange(ProcessModel(scene, mod));
            }
            visscene.Node = nodes.ToArray();

            // instance the scene
            var scene_instance = new Instance_Visual_Scene();
            scene_instance.URL = "#" + visscene.Name;

            _collada.Library_Visual_Scene = new Library_Visual_Scenes();
            _collada.Library_Visual_Scene.Visual_Scene = new Visual_Scene[] { visscene };

            _collada.Scene = new Scene();
            _collada.Scene.Visual_Scene = scene_instance;
            
            // initialize asset
            InitAsset();


            // save to file
            _collada.SaveToFile(filePath);

            // cleanup
            _usedIDs.Clear();
            _collada = null;
        }

        /// <summary>
        /// 
        /// </summary>
        private void InitAsset()
        {
            _collada.Asset = new Asset();
            _collada.Asset.Up_Axis = "Y_UP";
            _collada.Asset.Unit = new Asset_Unit()
            {
                Name = "Meter",
                Meter = 1
            };

            _collada.Asset.Contributor = new Asset_Contributor[1] { new Asset_Contributor()
            {
                Authoring_Tool = "IONET Exporter"
            } };
            _collada.Asset.Created = DateTime.Now;
            _collada.Asset.Modified = DateTime.Now;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="color"></param>
        /// <param name="tex"></param>
        /// <returns></returns>
        private FX_Common_Color_Or_Texture_Type GenerateTextureInfo(string sid, Vector4 color, IOTexture tex, List<New_Param> textureParams)
        {
            FX.Custom_Types.Texture texData = null;

            if(tex != null)
            {
                var surfaceString = AddImage(tex);

                var doc = new XmlDocument();

                var surfacenode = doc.CreateElement("surface", "http://www.collada.org/2005/11/COLLADASchema");
                surfacenode.SetAttribute("type", "2D");
                var init_node = doc.CreateElement("init_from", "http://www.collada.org/2005/11/COLLADASchema");
                init_node.InnerText = surfaceString;
                surfacenode.AppendChild(init_node);

                textureParams.Add(new New_Param()
                {
                    sID = surfaceString + "_surface",
                    Data = new XmlElement[] { surfacenode }
                });

                var samplernode = doc.CreateElement("sampler2D", "http://www.collada.org/2005/11/COLLADASchema");
                var sourceNode = doc.CreateElement("source", "http://www.collada.org/2005/11/COLLADASchema");
                sourceNode.InnerText = surfaceString + "_surface";
                samplernode.AppendChild(sourceNode);

                textureParams.Add(new New_Param()
                {
                    sID = surfaceString + "_sampler",
                    Data = new XmlElement[] { samplernode }
                });

                texData = new IONET.Collada.FX.Custom_Types.Texture()
                {
                    Textures = surfaceString + "_sampler",
                    TexCoord = "CHANNEL0",
                };
            }
            
            return new FX_Common_Color_Or_Texture_Type()
            {
                Color = new IONET.Collada.Core.Lighting.Color()
                { sID = sid, Value_As_String = $"{color.X} {color.Y} {color.Z} {color.W}" },

                Texture = _settings.ExportTextureInfo ? texData : null
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mat"></param>
        private void ProcessMaterial(IOMaterial mat)
        {
            List<New_Param> textureParams = new List<New_Param>();

            // create phong shading
            var phong = new Phong()
            {
                Shininess = new FX_Common_Float_Or_Param_Type { Float = new IONET.Collada.Types.SID_Float() { sID = "shininess", Value = mat.Shininess } },
                Transparency = new FX_Common_Float_Or_Param_Type() { Float = new IONET.Collada.Types.SID_Float() { sID = "transparency", Value = mat.Alpha } },
                Reflectivity = new FX_Common_Float_Or_Param_Type() { Float = new IONET.Collada.Types.SID_Float() { sID = "reflectivity", Value = mat.Reflectivity } },
                Ambient = GenerateTextureInfo("ambient", mat.AmbientColor, mat.AmbientMap, textureParams),
                Diffuse = GenerateTextureInfo("diffuse", mat.DiffuseColor, mat.DiffuseMap, textureParams),
                Specular = GenerateTextureInfo("specular", mat.SpecularColor, mat.SpecularMap, textureParams),
                Emission = GenerateTextureInfo("emission", mat.EmissionColor, mat.EmissionMap, textureParams),
                Reflective = GenerateTextureInfo("reflective", mat.ReflectiveColor, mat.ReflectiveMap, textureParams),
            };

            // create effect
            Effect effect = new Effect()
            {
                ID = GetUniqueID(mat.Name + "-effect"),
                Name = mat.Name,
                Profile_COMMON = new IONET.Collada.FX.Profiles.COMMON.Profile_COMMON[]
                {
                    new IONET.Collada.FX.Profiles.COMMON.Profile_COMMON()
                    {
                        Technique = new IONET.Collada.FX.Profiles.COMMON.Effect_Technique_COMMON()
                        {
                            sID = "standard",
                            Phong = phong
                        },
                        New_Param = textureParams.ToArray()
                    }
                }
            };

            // create material
            Material material = new Material()
            {
                ID = GetUniqueID(mat.Name),
                Name = mat.Name,
                Instance_Effect = new Instance_Effect()
                {
                    URL = "#" + effect.ID
                }
            };

            // add effect to effect library
            if (_collada.Library_Effects == null)
                _collada.Library_Effects = new Library_Effects();

            if (_collada.Library_Effects.Effect == null)
                _collada.Library_Effects.Effect = new Effect[0];

            Array.Resize(ref _collada.Library_Effects.Effect, _collada.Library_Effects.Effect.Length + 1);

            _collada.Library_Effects.Effect[_collada.Library_Effects.Effect.Length - 1] = effect;

            // add material to material library
            if (_collada.Library_Materials == null)
                _collada.Library_Materials = new Library_Materials();

            if (_collada.Library_Materials.Material == null)
                _collada.Library_Materials.Material = new Material[0];

            Array.Resize(ref _collada.Library_Materials.Material, _collada.Library_Materials.Material.Length + 1);

            _collada.Library_Materials.Material[_collada.Library_Materials.Material.Length - 1] = material;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private string AddImage(IOTexture tex)
        {
            var name = tex.Name;
            var filePath = tex.FilePath;

            // create image node
            Image img = new Image()
            {
                ID = GetUniqueID(name + "-image"),
                Name = name,
                Init_From = new Init_From()
                {
                    Value = filePath
                }
            };

            // add image element to image library
            if (_collada.Library_Images == null)
                _collada.Library_Images = new Library_Images();

            if (_collada.Library_Images.Image == null)
                _collada.Library_Images.Image = new Image[0];

            Array.Resize(ref _collada.Library_Images.Image, _collada.Library_Images.Image.Length + 1);

            _collada.Library_Images.Image[_collada.Library_Images.Image.Length - 1] = img;

            // return id
            return img.ID;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        private List<Node> ProcessNodeTree(IOScene scene, IOModel model)
        {
            List<Node> nodes = new List<Node>();

            List<string> bones = model.Meshes.SelectMany(x => x.Vertices.SelectMany(z => z.Envelope.Weights.Select(y => y.BoneName))).ToList();

            foreach (var n in scene.Nodes)
                if (n.Parent == null)
                    nodes.Add(ProcessNode(n, scene, model, bones));

            return nodes;
        }

        private Node ProcessNode(IONode node, IOScene scene, IOModel model, List<string> bones)
        {
            Node n = new Node()
            {
                Name = node.Name,
                sID = node.Name,
                ID = node.Name,
                Matrix = new Matrix[] { new Matrix() },
                Type = Node_Type.NODE
            };

            if (node.IsJoint || bones.Contains(n.Name))
                n.Type = Node_Type.JOINT;

            if (node.Mesh != null)
                n = ProcessMesh(node.Mesh, model, node.Parent);

            if (IsNodeAnimated(scene.Animations, n.ID))
            {
                Matrix4x4.Decompose(node.LocalTransform, out Vector3 scale, out Quaternion rotation, out Vector3 translation);

                var pos = translation;
                var rot = ToEulerAngles(rotation);
                var sca = scale;
               

                n.Matrix = null;
                n.Translate = new Translate[1];
                n.Rotate = new Rotate[3];
                n.Scale = new Scale[1];

                n.Translate[0] = new Translate() { sID = "location", Value_As_String = $"{pos.X} {pos.Y} {pos.Z}", };
                n.Rotate[0] = new Rotate() { sID = "rotationZ", Value_As_String = $"0 0 1 {MathExt.RadToDeg(rot.Z)}", };
                n.Rotate[1] = new Rotate() { sID = "rotationY", Value_As_String = $"0 1 0 {MathExt.RadToDeg(rot.Y)}", };
                n.Rotate[2] = new Rotate() { sID = "rotationX", Value_As_String = $"1 0 0 {MathExt.RadToDeg(rot.X)}", };
                n.Scale[0] = new Scale() { sID = "scale", Value_As_String = $"{sca.X} {sca.Y} {sca.Z}", };
            }
            else
                n.Matrix[0].FromMatrix(node.LocalTransform);

            n.node = new Node[node.Children.Length];

            int childIndex = 0;
            foreach (IONode child in node.Children)
                n.node[childIndex++] = ProcessNode(child, scene, model, bones);

            return n;
        }

        public static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static Vector3 ToEulerAngles(Quaternion q)
        {
            Matrix4x4 mat = Matrix4x4.CreateFromQuaternion(q);
            float x, y, z;
            y = (float)Math.Asin(Clamp(mat.M13, -1, 1));

            if (Math.Abs(mat.M13) < 0.99999)
            {
                x = (float)Math.Atan2(-mat.M23, mat.M33);
                z = (float)Math.Atan2(-mat.M12, mat.M11);
            }
            else
            {
                x = (float)Math.Atan2(mat.M32, mat.M22);
                z = 0;
            }
            return new Vector3(x, y, z) * -1;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        private List<Node> ProcessModel(IOScene scene, IOModel model)
        {
            List<Node> nodes = new List<Node>();

            // bones
            foreach (var root in model.Skeleton.RootBones)
                nodes.Add(ProcessSkeleton(scene, root));

            // get root bone
            IOBone rootBone = null;
            if (model.Skeleton != null && model.Skeleton.RootBones.Count > 0)
                rootBone = model.Skeleton.RootBones[0];

            // mesh
            foreach (var mesh in model.Meshes)
                nodes.Add(ProcessMesh(mesh, model, rootBone));

            return nodes;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bone"></param>
        /// <returns></returns>
        private Node ProcessSkeleton(IOScene scene, IOBone bone)
        {
            Node n = new Node()
            {
                Name = bone.Name,
                sID = bone.Name,
                ID = bone.Name,
                Matrix = new Matrix[] { new Matrix() },
                Type = Node_Type.JOINT
            };
            if (IsNodeAnimated(scene.Animations, n.ID))
            {
                var pos = bone.Translation;
                var rot = bone.RotationEuler;
                var sca = bone.Scale;

                n.Matrix = null;
                n.Translate = new Translate[1];
                n.Rotate = new Rotate[3];
                n.Scale = new Scale[1];

                n.Translate[0] = new Translate() { sID = "location", Value_As_String = $"{pos.X} {pos.Y} {pos.Z}", };
                n.Rotate[0] = new Rotate() { sID = "rotationX", Value_As_String = $"0 0 1 {MathExt.RadToDeg(rot.X)}", };
                n.Rotate[1] = new Rotate() { sID = "rotationY", Value_As_String = $"0 1 0 {MathExt.RadToDeg(rot.Y)}", };
                n.Rotate[2] = new Rotate() { sID = "rotationZ", Value_As_String = $"1 0 0 {MathExt.RadToDeg(rot.Z)}", };
                n.Scale[0] = new Scale() { sID = "scale", Value_As_String = $"{sca.X} {sca.Y} {sca.Z}", };
            }
            else
                n.Matrix[0].FromMatrix(bone.LocalTransform);

            n.node = new Node[bone.Children.Length];

            int childIndex = 0;
            foreach(var child in bone.Children)
                n.node[childIndex++] = ProcessSkeleton(scene, child);

            return n;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        private Node ProcessMesh(IOMesh mesh, IOModel model, IOBone rootBone)
        {
            string id = GetUniqueID(mesh.Name);

            Node n = new Node()
            {
                Name = mesh.Name,
                sID = id,
                ID = id,
                Matrix = new Matrix[] { new Matrix() },
                Type = Node_Type.NODE,
            };

            var materials = mesh.Polygons.Select(e => e.MaterialName).Distinct();

            if (mesh.HasEnvelopes())
            {
                var geom = new Instance_Controller();

                geom.URL = "#" + GenerateGeometryController(mesh, model.Skeleton);

                if (rootBone != null)
                    geom.Skeleton = new Skeleton[] { new Skeleton() { Value = "#" + rootBone.Name } };

                n.Instance_Controller = new Instance_Controller[] { geom };
                
                n.Instance_Controller[0].Bind_Material = new IONET.Collada.FX.Materials.Bind_Material[]
                {
                    new Bind_Material()
                    {
                        Technique_Common = new FX.Technique_Common.Technique_Common_Bind_Material()
                        {
                            Instance_Material = materials.Select(e => new Instance_Material_Geometry() { Symbol = e, Target = "#" + e }).ToArray()
                        }
                    }
                };
            }
            else
            {
                var geom = new Instance_Geometry();

                geom.URL = "#" + GenerateGeometry(mesh);

                if (mesh.Transform != Matrix4x4.Identity)
                {
                    n.Matrix = new Matrix[] { new Matrix() };
                    n.Matrix[0].FromMatrix(mesh.Transform);
                }

                n.Instance_Geometry = new Instance_Geometry[] { geom };

                n.Instance_Geometry[0].Bind_Material = new IONET.Collada.FX.Materials.Bind_Material[]
                {
                    new Bind_Material()
                    {
                        Technique_Common = new FX.Technique_Common.Technique_Common_Bind_Material()
                        {
                            Instance_Material = materials.Select(e => new Instance_Material_Geometry() { Symbol = e, Target = "#" + e }).ToArray()
                        }
                    }
                };
            }

            return n;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        private string GenerateGeometryController(IOMesh mesh, IOSkeleton skeleton)
        {
            Controller con = new Controller()
            {
                ID = GetUniqueID(mesh.Name + "-controller"),
                Name = mesh.Name
            };

            con.Skin = new Skin()
            {
                sourceid = "#" + GenerateGeometry(mesh)
            };

            con.Skin.Bind_Shape_Matrix = new IONET.Collada.Types.Float_Array_String()
            {
                Value_As_String = "1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1"
            };

            List<int> weightCounts = new List<int>();
            List<int> binds = new List<int>();

            List<string> boneNames = new List<string>();
            List<float> boneBinds = new List<float>();
            List<float> weights = new List<float>();

            foreach (var v in mesh.Vertices)
            {
                weightCounts.Add(v.Envelope.Weights.Count);
                foreach(var bw in v.Envelope.Weights)
                {
                    if (!boneNames.Contains(bw.BoneName))
                    {
                        boneNames.Add(bw.BoneName);
                        Matrix4x4.Invert(skeleton.GetBoneByName(bw.BoneName).WorldTransform, out Matrix4x4 mat);
                        boneBinds.AddRange(new float[] {
                            mat.M11, mat.M21, mat.M31, mat.M41,
                            mat.M12, mat.M22, mat.M32, mat.M42,
                            mat.M13, mat.M23, mat.M33, mat.M43,
                            mat.M14, mat.M24, mat.M34, mat.M44
                        });
                    }

                    if (!weights.Contains(bw.Weight))
                        weights.Add(bw.Weight);

                    binds.Add(boneNames.IndexOf(bw.BoneName));
                    binds.Add(weights.IndexOf(bw.Weight));
                }
            }

            if (_settings.BlenderMode)
            {
                // blender is so stupid
                // the binds need to be every so slightly different or it kills bones
                for (int i = 0; i < boneBinds.Count; i++)
                    boneBinds[i] += 0.000001f;
            }

            var mid = GetUniqueID(mesh.Name + "-matrices");
            var jid = GetUniqueID(mesh.Name + "-joints");
            var wid = GetUniqueID(mesh.Name + "-weights");

            var midarr = GetUniqueID(mesh.Name + "-matrices-array");
            var jidarr = GetUniqueID(mesh.Name + "-joints-array");
            var widarr = GetUniqueID(mesh.Name + "-weights-array");

            con.Skin.Source = new Source[]
            {
                new Source()
                {
                    ID = jid,
                    Name_Array = new Name_Array()
                    {
                        Count = boneNames.Count,
                        ID = jidarr,
                        Value_Pre_Parse = string.Join(" ", boneNames)
                    },
                    Technique_Common = new IONET.Collada.Core.Technique_Common.Technique_Common_Source()
                    {
                        Accessor = new Accessor()
                        {
                            Count = (uint)boneNames.Count,
                            Source =  "#" + jidarr,
                            Param = new IONET.Collada.Core.Parameters.Param[]
                            {
                                new IONET.Collada.Core.Parameters.Param()
                                {
                                    Name = "JOINT",
                                    Type = "Name"
                                }
                            },
                            Stride = 1
                        }
                    }
                },
                new Source()
                {
                    ID = mid,
                    Float_Array = new Float_Array()
                    {
                        Count = boneBinds.Count,
                        ID = midarr,
                        Value_As_String = string.Join(" ", boneBinds)
                    },
                    Technique_Common = new IONET.Collada.Core.Technique_Common.Technique_Common_Source()
                    {
                        Accessor = new Accessor()
                        {
                            Count = (uint)boneBinds.Count / 16,
                            Source =  "#" + midarr,
                            Param = new IONET.Collada.Core.Parameters.Param[]
                            {
                                new IONET.Collada.Core.Parameters.Param()
                                {
                                    Name = "TRANSFORM",
                                    Type = "float4x4"
                                }
                            },
                            Stride = 16
                        }
                    }
                },
                new Source()
                {
                    ID = wid,
                    Float_Array = new Float_Array()
                    {
                        Count = weights.Count,
                        ID = widarr,
                        Value_As_String = string.Join(" ", weights)
                    },
                    Technique_Common = new IONET.Collada.Core.Technique_Common.Technique_Common_Source()
                    {
                        Accessor = new Accessor()
                        {
                            Count = (uint)weights.Count,
                            Source =  "#" + widarr,
                            Param = new IONET.Collada.Core.Parameters.Param[]
                            {
                                new IONET.Collada.Core.Parameters.Param()
                                {
                                    Name = "WEIGHT",
                                    Type = "float"
                                }
                            },
                            Stride = 1
                        }
                    }
                },
            };

            con.Skin.Joints = new Joints()
            {
                Input = new Input_Unshared[]
                {
                    new Input_Unshared()
                    {
                        Semantic = Input_Semantic.JOINT,
                        source = "#" + jid
                    },
                    new Input_Unshared()
                    {
                        Semantic = Input_Semantic.INV_BIND_MATRIX,
                        source = "#" + mid
                    },
                }
            };

            con.Skin.Vertex_Weights = new Vertex_Weights()
            {
                Count = weightCounts.Count,
                V = new IONET.Collada.Types.Int_Array_String()
                {
                    Value_As_String = string.Join(" ", binds)
                },
                VCount = new IONET.Collada.Types.Int_Array_String()
                {
                    Value_As_String = string.Join(" ", weightCounts)
                },
                Input = new Input_Shared[]
                {
                    new Input_Shared()
                    {
                        Semantic = Input_Semantic.JOINT,
                        source = "#" + jid,
                        Offset = 0
                    },
                    new Input_Shared()
                    {
                        Semantic = Input_Semantic.WEIGHT,
                        source = "#" + wid,
                        Offset = 1
                    },
                }
            };


            // add geometry element to document
            if (_collada.Library_Controllers == null)
                _collada.Library_Controllers = new Library_Controllers();

            if (_collada.Library_Controllers.Controller == null)
                _collada.Library_Controllers.Controller = new Controller[0];

            Array.Resize(ref _collada.Library_Controllers.Controller, _collada.Library_Controllers.Controller.Length + 1);

            _collada.Library_Controllers.Controller[_collada.Library_Controllers.Controller.Length - 1] = con;

            return con.ID;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private string GenerateGeometry(IOMesh mesh)
        {
            // convert mesh to triangles to simplify
            mesh.MakeTriangles();

            // create a unique geometry id
            var geomID = GetUniqueID(mesh.Name + "-geometry");

            // create geometry element
            Geometry geom = new Geometry()
            {
                ID = geomID,
                Name = mesh.Name
            };

            geom.Mesh = new Mesh();

            // generate sources
            SourceGenerator srcgen = new SourceGenerator();

            srcgen.AddSourceData(
                geomID, Input_Semantic.POSITION,
                mesh.Vertices.SelectMany(e => new float[] { e.Position.X, e.Position.Y, e.Position.Z }).ToArray());

            srcgen.AddSourceData(
                geomID, Input_Semantic.NORMAL,
                mesh.Vertices.SelectMany(e => new float[] { e.Normal.X, e.Normal.Y, e.Normal.Z }).ToArray());

            for (int i = 0; i < 7; i++)
                if (mesh.HasUVSet(i))
                {
                    srcgen.AddSourceData(
                        geomID, Input_Semantic.TEXCOORD,
                        mesh.Vertices.SelectMany(e => new float[] { e.UVs[i].X, e.UVs[i].Y }).ToArray(),
                        i);
                }

            for (int i = 0; i < 7; i++)
                if (mesh.HasColorSet(i))
                {
                    srcgen.AddSourceData(
                        geomID, Input_Semantic.COLOR,
                        mesh.Vertices.SelectMany(e => new float[] { e.Colors[i].X, e.Colors[i].Y, e.Colors[i].Z, e.Colors[i].W }).ToArray(),
                        i);
                }

            // fill in vertex info
            geom.Mesh.Vertices = new Vertices()
            {
                ID = GetUniqueID(mesh.Name + "-vertices"),
                Input = new Input_Unshared[]{
                    new Input_Unshared()
                    {
                        Semantic = IONET.Collada.Enums.Input_Semantic.POSITION,
                        source = "#" + srcgen.GetID(Input_Semantic.POSITION)
                    }
                }
            };

            // fill in triangles

            var polyIndex = 0;
            geom.Mesh.Triangles = new Triangles[mesh.Polygons.Count];
            foreach (var poly in mesh.Polygons)
            {
                if (poly.PrimitiveType != IOPrimitive.TRIANGLE)
                {
                    System.Diagnostics.Debug.WriteLine("Warning: " + poly.PrimitiveType + " not currently supported");
                    continue;
                }

                Triangles tri = new Triangles()
                {
                    Count = poly.Indicies.Count / 3,
                    Material = poly.MaterialName
                };
                
                List<Input_Shared> inputs = new List<Input_Shared>();
                inputs.Add(new Input_Shared()
                {
                    Semantic = Input_Semantic.VERTEX,
                    Offset = inputs.Count,
                    source = "#" + geom.Mesh.Vertices.ID
                });

                inputs.Add(new Input_Shared()
                {
                    Semantic = Input_Semantic.NORMAL,
                    Offset = inputs.Count,
                    source = "#" + srcgen.GetID(Input_Semantic.NORMAL)
                });
                
                for (int i = 0; i < 7; i++)
                    if (mesh.HasUVSet(i))
                    {
                        inputs.Add(new Input_Shared()
                        {
                            Semantic = Input_Semantic.TEXCOORD,
                            source = "#" + srcgen.GetID(Input_Semantic.TEXCOORD, i),
                            Offset = inputs.Count,
                            Set = i
                        });
                    }

                for (int i = 0; i < 7; i++)
                    if (mesh.HasColorSet(i))
                    {
                        inputs.Add(new Input_Shared()
                        {
                            Semantic = Input_Semantic.COLOR,
                            source = "#" + srcgen.GetID(Input_Semantic.COLOR, i),
                            Offset = inputs.Count,
                            Set = i
                        });
                    }

                tri.Input = inputs.ToArray();

                tri.P = new IONET.Collada.Types.Int_Array_String()
                {
                    Value_As_String = string.Join(" ", srcgen.Remap(poly.Indicies))
                };
                
                geom.Mesh.Triangles[polyIndex++] = tri;
            }


            // generate sources
            geom.Mesh.Source = srcgen.GetSources();


            // add geometry element to document
            if (_collada.Library_Geometries ==null)
                _collada.Library_Geometries = new Library_Geometries();

            if (_collada.Library_Geometries.Geometry == null)
                _collada.Library_Geometries.Geometry = new Geometry[0];

            Array.Resize(ref _collada.Library_Geometries.Geometry, _collada.Library_Geometries.Geometry.Length + 1);

            _collada.Library_Geometries.Geometry[_collada.Library_Geometries.Geometry.Length - 1] = geom;

            // return geometry id
            return geomID;
        }

        public bool IsNodeAnimated(List<IOAnimation> animations, string id)
        {
            foreach (var anim in animations)
            {
                if (anim.Name == id)
                    return true;

                if (IsNodeAnimated(anim.Groups, id))
                    return true;
            }

            return false;
        }

        private void ProcessAnimation(IOAnimation anim)
        {
            //Allow multiple groups to be creatable from a single animation incase we want to split them (like decompsing a matrix4x4)
            foreach (Animation processSubAnimation in ProcessSubAnimations(null, anim)[0].Animations)
                this.AddAnimation(processSubAnimation);
        }

        private void AddAnimation(IONET.Collada.Core.Animation.Animation animation)
        {
            if (this._collada.Library_Animations == null)
                this._collada.Library_Animations = new Library_Animations();
            if (this._collada.Library_Animations.Animation == null)
                this._collada.Library_Animations.Animation = new IONET.Collada.Core.Animation.Animation[0];
            Array.Resize<IONET.Collada.Core.Animation.Animation>(ref this._collada.Library_Animations.Animation, this._collada.Library_Animations.Animation.Length + 1);
            this._collada.Library_Animations.Animation[this._collada.Library_Animations.Animation.Length - 1] = animation;
        }

        private List<Animation> ProcessSubAnimations(Animation parentAnimation, IOAnimation group)
        {
            List<Animation> animationList = new List<Animation>();
            foreach (IOAnimationTrack track in group.Tracks)
            {
                string channelTarget = this.GetChannelOutputParam(track.ChannelType);
                //Get a unique name for the track
                string name = this.GetUniqueID($"{group.Name}_{GetChannelTarget(track.ChannelType).Replace(".", "_")}");
                //Create an animation to store track info
                Animation animation = new Animation()
                {
                    ID = name,
                    Animations = new Animation[0]
                };
                animationList.Add(animation);
                //Sampler for determining the inputs of the track
                Sampler sampler = new Sampler()
                {
                    ID = $"{name}" + "-sampler",
                    Input = new Input_Unshared[3]
                };
                //Channel for determining the target of the animation
                Channel channel = new Channel()
                {
                    Source = $"#{sampler.ID}",
                    Target = $"{group.Name}/{this.GetChannelTarget(track.ChannelType)}"
                };
                //Setup the track data
                float[] timeValues = new float[track.KeyFrames.Count];
                string[] interpolationModes = new string[track.KeyFrames.Count];
                //Get the key data
                List<float> outputValues = new List<float>();
                for (int f = 0; f < track.KeyFrames.Count; ++f)
                {
                    //Instead of frames, get the time value based on the frame rate.
                    timeValues[f] = track.KeyFrames[f].Frame / this._settings.FrameRate;
                    //Track types can be a 4x4 matrix (float[16]) or a single channel value
                    if (track.KeyFrames[f].Value is float[])
                        outputValues.AddRange((float[])track.KeyFrames[f].Value);
                    else
                        outputValues.Add((float)track.KeyFrames[f].Value);
                    //Interpolation modes based on the provided key frame type
                    if (track.KeyFrames[f] is IOKeyFrameBezier)
                        interpolationModes[f] = "BEZIER";
                    else if(track.KeyFrames[f] is IOKeyFrameHermite) 
                        interpolationModes[f] = "HERMITE";
                    else
                        interpolationModes[f] = "LINEAR";
                    //TODO
                    interpolationModes[f] = "LINEAR";
                }

                //Convert rotation to degrees
                if (track.ChannelType == IOAnimationTrackType.RotationEulerX || 
                    track.ChannelType == IOAnimationTrackType.RotationEulerY ||
                    track.ChannelType == IOAnimationTrackType.RotationEulerZ)
                {
                    for (int i = 0; i < outputValues.Count; i++)
                        outputValues[i] = MathExt.RadToDeg(outputValues[i]);
                }

                //Inputs to link to the source data
                List<Input_Unshared> inputs = new List<Input_Unshared>();
                inputs.Add(new Input_Unshared() { Semantic = Input_Semantic.INPUT, source = $"#{name}-input" });
                inputs.Add(new Input_Unshared() { Semantic = Input_Semantic.OUTPUT, source = $"#{name}-output" });
                inputs.Add(new Input_Unshared() { Semantic = Input_Semantic.INTERPOLATION, source = $"#{name}-interpolation" });

                bool hasTangents = interpolationModes.Any(x => x == "HERMITE") || 
                                   interpolationModes.Any(x => x == "BEZIER");
                //Tangent inputs
                if (hasTangents) {
                    inputs.Add(new Input_Unshared() { Semantic = Input_Semantic.IN_TANGENT, source = $"#{name}-intangent" });
                    inputs.Add(new Input_Unshared() { Semantic = Input_Semantic.OUT_TANGENT, source = $"#{name}-outtangent" });
                }
                //Sources for raw data
                List<Source> sourceList = new List<Source>();
                sourceList.Add(CreateAnimationSource($"{name}-input", timeValues, CreateAnimParam("TIME", "float")));
                sourceList.Add(CreateAnimationSource($"{name}-output", outputValues.ToArray(), CreateAnimParam(channelTarget, "float")));
                sourceList.Add(CreateAnimationSource($"{name}-interpolation", interpolationModes, CreateAnimParam("INTERPOLATION", "name")));
                //Tangent sources
                if (hasTangents)
                {
                    List<float> tagentInputs = new List<float>();
                    List<float> tangentOutputs = new List<float>();
                    for (int index = 0; index < track.KeyFrames.Count; ++index)
                    {
                        if (interpolationModes[index] == "BEZIER")
                        {
                            IOKeyFrameBezier keyFrame = track.KeyFrames[index] as IOKeyFrameBezier;
                            tagentInputs.Add(keyFrame.TangentInputX);
                            tagentInputs.Add(keyFrame.TangentInputY);
                            tangentOutputs.Add(keyFrame.TangentOutputX);
                            tangentOutputs.Add(keyFrame.TangentOutputY);
                        }
                        if (interpolationModes[index] == "HERMITE")
                        {
                            IOKeyFrameHermite keyFrame = track.KeyFrames[index] as IOKeyFrameHermite;
                            tagentInputs.Add(keyFrame.TangentSlopeInput);
                            tangentOutputs.Add(keyFrame.TangentSlopeOutput);
                            tagentInputs.Add(0.0f);
                            tangentOutputs.Add(0.0f);
                        }
                    }
                    Param[] parameters = new Param[2]
                    {
                        CreateAnimParam("X", "float"), CreateAnimParam("Y", "float")
                    };
                    sourceList.Add(CreateAnimationSource($"{name}-intangent", tagentInputs.ToArray(), parameters));
                    sourceList.Add(CreateAnimationSource($"{name}-outtangent", tangentOutputs.ToArray(), parameters));
                }
                sampler.Input = inputs.ToArray();
                animation.Sampler = new Sampler[1] { sampler };
                animation.Channel = new Channel[1] { channel };
                animation.Source = sourceList.ToArray();
            }

            string groupName = this.GetUniqueID(group.Name);
            if (parentAnimation != null)
                groupName = $"{GetUniqueID(parentAnimation.ID)}__{group.Name}";

            if (group.Groups.Count > 0)
            {
                Animation animGroup = new Animation()
                {
                    ID = groupName,
                    Name = group.Name,
                    Animations = animationList.ToArray()
                };

                foreach (IOAnimation gr in group.Groups)
                    animationList.AddRange(ProcessSubAnimations(animGroup, gr));
                animGroup.Animations = animationList.ToArray();

                return new List<Animation>() { animGroup };
            }
            else
                return animationList;
        }

        private static Source CreateAnimationSource<T>(string id, T[] value, params Param[] parameters)
        {
            Source source = new Source() { ID = id };
            uint stride = (uint)parameters.Length;
            if (parameters[0].Type == "name")
            {
                source.Name_Array = new Name_Array()
                {
                    Count = value.Length,
                    ID = $"{id}-array",
                    Value_Pre_Parse = string.Join(" ", value as string[])
                };
            }
            if (parameters[0].Type == "float")
            {
                source.Float_Array = new Float_Array()
                {
                    Count = value.Length,
                    ID = $"{id}-array",
                    Value_As_String = string.Join(" ", value as float[])
                };
            }
            if (parameters[0].Type == "transform")
            {
                stride = 16;
                source.Float_Array = new Float_Array()
                {
                    Count = value.Length,
                    ID = $"{id}-array",
                    Value_As_String = string.Join(" ", value as float[])
                };
            }
            source.Technique_Common = new Technique_Common_Source()
            {
                Accessor = new Accessor()
                {
                    Count = (uint)value.Length,
                    Source = "#" + id + "-array",
                    Param = parameters,
                    Stride = stride
                }
            };
            return source;
        }

        private Param CreateAnimParam(string name, string type) {
            return new Param() { Name = name, Type = type };
        }

        private string GetChannelTarget(IOAnimationTrackType type)
        {
            switch (type)
            {
                case IOAnimationTrackType.PositionX: return "location.X";
                case IOAnimationTrackType.PositionY: return "location.Y";
                case IOAnimationTrackType.PositionZ: return "location.Z";
                case IOAnimationTrackType.RotationEulerX: return "rotationX.ANGLE";
                case IOAnimationTrackType.RotationEulerY: return "rotationY.ANGLE";
                case IOAnimationTrackType.RotationEulerZ: return "rotationZ.ANGLE";
                case IOAnimationTrackType.ScaleX: return "scale.X";
                case IOAnimationTrackType.ScaleY: return "scale.Y";
                case IOAnimationTrackType.ScaleZ: return "scale.Z";
                case IOAnimationTrackType.TransformMatrix4x4: return "transform";
                default:
                    throw new Exception(string.Format("Unsupported track type {0}.", (object)type));
            }
        }

        private string GetChannelOutputParam(IOAnimationTrackType type)
        {
            switch (type)
            {
                case IOAnimationTrackType.PositionX: return "X";
                case IOAnimationTrackType.PositionY: return "Y";
                case IOAnimationTrackType.PositionZ: return "Z";
                case IOAnimationTrackType.RotationEulerX: return "ANGLE";
                case IOAnimationTrackType.RotationEulerY: return "ANGLE";
                case IOAnimationTrackType.RotationEulerZ: return "ANGLE";
                case IOAnimationTrackType.ScaleX: return "X";
                case IOAnimationTrackType.ScaleY: return "Y";
                case IOAnimationTrackType.ScaleZ: return "Z";
                case IOAnimationTrackType.TransformMatrix4x4: return "TRANSFORM";
                default:
                    throw new Exception(string.Format("Unsupported track type {0}.", (object)type));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string[] GetExtensions()
        {
            return new string[] { ".dae" };
        }
        
    }
}
