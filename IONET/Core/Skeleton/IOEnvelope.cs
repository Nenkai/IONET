using IONET.Collada.Core.Transform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace IONET.Core
{
    /// <summary>
    /// Bind information for bone to vertex
    /// </summary>
    public class IOEnvelope
    {
        /// <summary>
        /// Weights for this vertex
        /// </summary>
        public List<IOBoneWeight> Weights { get; set; } = new List<IOBoneWeight>();

        /// <summary>
        /// Indicates if the BindMatrix in <see cref="IOBoneWeight"/> is meant to be used
        /// </summary>
        public bool UseBindMatrix { get; set; } = false;

        /// <summary>
        /// Optimizes number of weights by removing weights with lesser influence
        /// </summary>
        public void Optimize(int maxWeights)
        {
            var optimizedWeights = Optimize(Weights, maxWeights);

            Weights.Clear();

            Weights.AddRange(optimizedWeights);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="weights"></param>
        /// <param name="weightCount"></param>
        /// <returns></returns>
        private static IOBoneWeight[] Optimize(List<IOBoneWeight> weights, int maxWeights)
        {
            IOBoneWeight[] optimized = new IOBoneWeight[maxWeights];

            if (weights.Count > maxWeights)
            {
                int[] toRemove = new int[weights.Count - maxWeights];

                for (int i = 0; i < toRemove.Length; ++i)
                    for (int j = 0; j < weights.Count; ++j)
                        if (!toRemove.Contains(j + 1) &&
                            (toRemove[i] == 0 || weights[j].Weight < weights[toRemove[i] - 1].Weight))
                            toRemove[i] = j + 1;

                foreach (int k in toRemove)
                    weights.RemoveAt(k - 1);
            }

            for (int i = 0; i < weights.Count; ++i)
                optimized[i] = weights[i];

            Normalize(optimized);

            return optimized;
        }
        public void Normalize() => Normalize(this.Weights);
        public void NormalizeByteType(float scale = 0.01f) => NormalizeByteType(this.Weights, scale);

        /// <summary>
        /// Makes sure all weights add up to 1.0f.
        /// Does not modify any locked weights.
        /// </summary>
        private static void Normalize(IEnumerable<IOBoneWeight> weights, int weightDecimalPlaces = 7)
        {
            float max_precent = 1.0f;
            List<IOBoneWeight> list = weights.ToList<IOBoneWeight>();

            int weightID = 0;
            foreach (IOBoneWeight weight in weights)
            {
                ++weightID;
                if (weight != null)
                {
                    float weightBase = weight.Weight;
                    if (list.Count == weightID) //If last weight
                        weightBase = max_precent; //Apply the last filled percentile
                    if (weightBase >= max_precent) //If weights go over max then clamp it
                    {
                        weightBase = max_precent;
                        max_precent = 0;
                    }
                    else //Lower the precent by each provided weight
                        max_precent -= weightBase;
                    weight.Weight = weightBase;
                }
            }
        }
        public void LimtSkinCount(int max)
        {
            if (this.Weights.Count <= max)
                return;

            this.Weights = this.Weights.OrderByDescending(x => x.Weight).ToList();

            List<IOBoneWeight> ioBoneWeightList = new List<IOBoneWeight>();
            for (int index = 0; index < this.Weights.Count; ++index)
            {
                if (index >= max)
                    ioBoneWeightList.Add(this.Weights[index]);
            }
            foreach (IOBoneWeight ioBoneWeight in ioBoneWeightList)
                this.Weights.Remove(ioBoneWeight);
            IOEnvelope.Normalize((IEnumerable<IOBoneWeight>)this.Weights);
        }

        private static void NormalizeByteType(IEnumerable<IOBoneWeight> weights, float scale = 0.01f)
        {
            if (scale == 1.0f)
            {
                IOEnvelope.Normalize(weights);
                return;
            }

            int max_precent = (int)(1.0f / scale);
            List<IOBoneWeight> list = weights.ToList<IOBoneWeight>();

            int weightID = 0;
            foreach (IOBoneWeight weight in weights)
            {
                ++weightID;
                if (weight != null)
                {
                    int weightInt = (int)MathF.Round(weight.Weight / scale);
                    if (list.Count == weightID) //If last weight
                        weightInt = max_precent; //Apply the last filled percentile
                    if (weightInt >= max_precent) //If weights go over max then clamp it
                    {
                        weightInt = max_precent;
                        max_precent = 0;
                    }
                    else //Lower the precent by each provided weight
                        max_precent -= weightInt;
                    weight.Weight = weightInt * scale;
                }
            }
           // Console.WriteLine($"Normalized weights {string.Join(',', normalized)}");
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class IOBoneWeight
    {
        /// <summary>
        /// Name of Bone to be influenced by
        /// </summary>
        public string BoneName;

        /// <summary>
        /// 
        /// </summary>
        public Matrix4x4 BindMatrix = Matrix4x4.Identity;

        /// <summary>
        /// Amount of Weight to this bone
        /// </summary>
        public float Weight;

        public override string ToString()
        {
            return $"{BoneName} {Weight}";
        }
    }
}
