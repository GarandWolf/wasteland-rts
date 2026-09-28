using UnityEngine;

namespace Wasteland.Presentation
{
    /// <summary>
    /// GameObject-only helper for Unity 6.3 Renderer Shader User Value.
    /// Units rendered as entities use <see cref="TeamColorIndex"/> instead.
    /// </summary>
    public static class RendererTeamUserValue
    {
        public static void Apply(Renderer renderer, uint packed)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.SetShaderUserValue(packed);
        }
    }
}
