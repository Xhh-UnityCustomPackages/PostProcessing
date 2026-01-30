#ifndef VOLUMETRIC_COMMONFUNCTIONS
#define VOLUMETRIC_COMMONFUNCTIONS




#if defined(SHADER_STAGE_COMPUTE) || defined(SHADER_STAGE_RAY_TRACING)
    #if defined(UNITY_STEREO_INSTANCING_ENABLED)
        #define UNITY_XR_ASSIGN_VIEW_INDEX(viewIndex) unity_StereoEyeIndex = viewIndex;
    #else
        #define UNITY_XR_ASSIGN_VIEW_INDEX(viewIndex)
    #endif

    // Backward compatibility
    #define UNITY_STEREO_ASSIGN_COMPUTE_EYE_INDEX   UNITY_XR_ASSIGN_VIEW_INDEX
#endif

#endif