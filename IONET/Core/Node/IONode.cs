using IONET.Core.Camera;
using IONET.Core.Light;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using System;
using System.Collections.Generic;
using System.Text;

namespace IONET.Core
{
    public class IONode : IOBone
    {
        public bool IsJoint = false;

        public IOMesh Mesh { get; set; }
        public IOCamera Camera { get; set; }
        public IOLight Light { get; set; }
    }
}
