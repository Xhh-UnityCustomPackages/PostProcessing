using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.PostProcessing
{
    public class PostProcessingUtils
    {
        public static readonly string packagePath = "Packages/com.game.rendering.postprocessing";


        #region Math
        public static float Exp2(float x)
        {
            return Mathf.Exp(x * 0.69314718055994530941723212145818f);
        }
        #endregion Math
    }
}
