using System;
using UnityEngine;

namespace Wasteland.Prototypes
{
    [CreateAssetMenu(fileName = "VatBakeProfile", menuName = "Wasteland/VAT Bake Profile")]
    public sealed class VatBakeProfile : ScriptableObject
    {
        public GameObject SourcePrefab;
        public VatBakeClip[] Clips =
        {
            new VatBakeClip { Name = "idle", Fps = 30f, Loop = true },
            new VatBakeClip { Name = "walk", Fps = 30f, Loop = true },
            new VatBakeClip { Name = "fire", Fps = 30f, Loop = true }
        };
        public string OutputFolder = "Assets/_Project/Prototypes/Generated/VAT";
        public int TextureWidth = 256;
        public string AssetName = "PlaceholderInfantry";
    }

    [Serializable]
    public sealed class VatBakeClip
    {
        public string Name = "idle";
        public AnimationClip Clip;
        public float Fps = 30f;
        public bool Loop = true;
    }
}
