using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace DCFApixels.WhimTex
{
    // Public Unity build callback, not a replacement for the user's build handler.
    internal sealed class WhimTexDocumentBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            try { WhimTexDocumentSession.PrepareForBuild(); }
            catch (System.Exception error)
            {
                throw new BuildFailedException("WhimTex Live Update recovery failed. Build cancelled: " + error.Message);
            }
        }
    }
}
