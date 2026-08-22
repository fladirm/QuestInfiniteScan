using UnityEngine;

namespace Genesis.RoomScan
{
    /// <summary>
    /// Detached interoperable mesh/PBR-page payload used by the generic GLB codecs.
    /// It is a derived export value, never canonical reconstruction state.
    /// </summary>
    internal struct RefinedTextureResult
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] UVs;
        public int[] Indices;
        public byte[] AtlasPixels;
        public byte[] NormalPixels;
        public int AtlasWidth;
        public int AtlasHeight;
    }
}
