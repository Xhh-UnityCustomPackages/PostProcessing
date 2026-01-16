using UnityEngine.Rendering.Universal;

namespace Game.Core.PostProcessing
{
    public static class ShaderKeywords
    {
        public const string _CONTACT_SHADOWS = "_CONTACT_SHADOWS";
        public const string _DEFERRED_RENDERING_PATH = "_DEFERRED_RENDERING_PATH";//用来给那些支持多个渲染路径的效果 提供关键字
    }
    
    //全局变量&关键字
    static class PipelineShaderIDs
    {
        public static readonly int _DepthMipChain = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _DepthPyramid = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _DepthPyramidMipLevelOffsets = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _ColorPyramidTexture = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _CameraPreviousColorTexture = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _MotionVectorTexture = MemberNameHelpers.ShaderPropertyID();
        
        public static readonly int ShaderVariablesGlobal = MemberNameHelpers.ShaderPropertyID();
        public static readonly int _ColorPyramidUvScaleAndLimitPrevFrame = MemberNameHelpers.ShaderPropertyID();
    }

    public static class PostProcessingRenderPassEvent
    {
        public const RenderPassEvent SetGlobalVariablesPass = RenderPassEvent.AfterRenderingPrePasses + 0;
        // ================================= Depth Prepass ================================================ //
        // Screen space effect need ignore transparent post depth since normal is not matched with depth.
        public const RenderPassEvent DepthPyramidPass = RenderPassEvent.AfterRenderingGbuffer + 1;//这个目前使用RenderGraph下顺序又说不同 反正应该实在CopyDepth之后
        // ==================================== Transparency =============================================== //

        public const RenderPassEvent ColorPyramidPass = RenderPassEvent.AfterRenderingTransparents + 4;
    }
}