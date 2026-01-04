namespace Game.Core.PostProcessing
{
    public class PostProcessFeatureContext
    {
        private int m_FrameCount = 0;

        public int FrameCount => m_FrameCount;

        public void UpdateFrame()
        {
            m_FrameCount++;
            if (m_FrameCount >= int.MaxValue)
            {
                m_FrameCount = 0;
            }
        }
    }
}