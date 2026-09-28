using UnityEngine;

namespace Wasteland.Presentation
{
    internal static class PresentationRenderBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BindGlobals()
        {
            TeamPaletteRuntime.BindGlobals();
        }
    }
}
