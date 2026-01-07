using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.PostProcessing
{
    static class PipelineShaderIDs
    {
        public static readonly int _DepthMipChain = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _DepthPyramidConstants = MemberNameHelpers.ShaderPropertyID();
    }

    public class PostProcessingUtils
    {
        public static readonly string packagePath = "Packages/com.game.rendering.postprocessing";


        #region Math
        public static float Exp2(float x)
        {
            return Mathf.Exp(x * 0.69314718055994530941723212145818f);
        }


        internal static float InterpolateOrientation(float fromValue, float toValue, float t)
        {
            // Compute the direct distance
            float directDistance = Mathf.Abs(toValue - fromValue);
            float outputValue = 0.0f;

            // Handle the two cases
            if (fromValue < toValue)
            {
                float upperRange = 360.0f - toValue;
                float lowerRange = fromValue;
                float alternativeDistance = upperRange + lowerRange;
                if (alternativeDistance < directDistance)
                {
                    float targetValue = toValue - 360.0f;
                    outputValue = fromValue + (targetValue - fromValue) * t;
                    if (outputValue < 0.0f)
                        outputValue += 360.0f;
                }
                else
                {
                    outputValue = fromValue + (toValue - fromValue) * t;
                }
            }
            else
            {
                float upperRange = 360.0f - fromValue;
                float lowerRange = toValue;
                float alternativeDistance = upperRange + lowerRange;
                if (alternativeDistance < directDistance)
                {
                    float targetValue = toValue + 360.0f;
                    outputValue = fromValue + (targetValue - fromValue) * t;
                    if (outputValue > 360.0f)
                        outputValue -= 360.0f;
                }
                else
                {
                    outputValue = fromValue + (toValue - fromValue) * t;
                }
            }

            return outputValue;
        }

        #endregion Math
    }
}
