using System;
using UnityEngine;
using static src.Utils.Voxels;
using static src.Utils.Voxels.Face;

namespace src.Model
{
    public class BlockType
    {
        public readonly uint id;
        public readonly string name;
        public readonly bool isSolid;
        public readonly int[] textures = new int[FACES.Length];
        public readonly Color32? color;

        public BlockType(uint id, string name, bool isSolid,
            int backTexture, int rightTexture,
            int frontTexture, int leftTexture,
            int bottomTexture, int topTexture)
        {
            this.id = id;
            this.name = name;
            this.isSolid = isSolid;
            textures[BACK.index] = backTexture;
            textures[RIGHT.index] = rightTexture;
            textures[FRONT.index] = frontTexture;
            textures[LEFT.index] = leftTexture;
            textures[BOTTOM.index] = bottomTexture;
            textures[TOP.index] = topTexture;
        }

        public BlockType(uint id, Color32 color, string name)
        {
            this.id = id;
            this.color = color;
            this.name = name;
            isSolid = true;
        }

        public int GetTextureID(Face face)
        {
            return textures[face.index];
        }

        public Sprite GetIcon(bool failed = false)
        {
            return failed ? 
                Resources.Load<Sprite>("BlockIcons/failed") :
                Resources.Load<Sprite>("BlockIcons/" + name);
        }
    }
}