using src.Model;
using src.Utils;

namespace src.AssetsInventory.Models
{
    public class SlotInfo
    {
        public Asset asset { get; set; }
        public BlockType block { get; set; }

        public SlotInfo(Asset asset)
        {
            this.asset = asset;
        }

        public SlotInfo(BlockType block)
        {
            this.block = block;
        }

        public SlotInfo()
        {
        }

        public override bool Equals(object obj)
        {
            if (obj == this) return true;
            if (obj == null || GetType() != obj.GetType())
                return false;

            if (obj is SlotInfo slotInfo)
                return (slotInfo.asset != null && asset != null && slotInfo.asset.id.Value == asset.id.Value)
                       || (slotInfo.block != null && block != null && slotInfo.block.id == block.id);
            return false;
        }
    }

    public class SerializableSlotInfo
    {
        public Asset asset { get; set; }
        public uint blockId { get; set; }

        public static SerializableSlotInfo FromSlotInfo(SlotInfo slotInfo)
        {
            var s = new SerializableSlotInfo
            {
                asset = slotInfo.asset
            };
            if (slotInfo.block != null)
                s.blockId = slotInfo.block.id;
            return s;
        }

        public SlotInfo ToSlotInfo()
        {
            var s = new SlotInfo
            {
                asset = asset
            };
            if (blockId != 0)
                s.block = Blocks.GetBlockType(blockId);
            return s;
        }
    }
}