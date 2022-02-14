using System;
using Newtonsoft.Json;
using src;
using src.MetaBlocks.MarkerBlock;
using src.Service;
using src.Utils;
using UnityEngine;

public partial class UtopiaApi : MonoBehaviour
{
    public Player player;

    public string PlaceBlock(String request)
    {
        var req = JsonConvert.DeserializeObject<PlaceBlockRequest>(request);
        var placed = player.PutBlock(new Vector3(req.position.x, req.position.y, req.position.z),
            VoxelService.INSTANCE.GetBlockType(req.type), true);
        return JsonConvert.SerializeObject(placed);
    }

    public string GetPlayerPosition()
    {
        var pos = Player.INSTANCE.transform.position;
        return JsonConvert.SerializeObject(SerializableVector3.From(pos));
    }

    public string GetMarkers()
    {
        return JsonConvert.SerializeObject(VoxelService.INSTANCE.GetMarkers());
    }

    public string GetPlayerLands(string walletId)
    {
        return JsonConvert.SerializeObject(VoxelService.INSTANCE.GetLandsFor(walletId));
    }

    public string GetBlockTypes()
    {
        return JsonConvert.SerializeObject(VoxelService.INSTANCE.GetBlockTypes());
    }

    private class PlaceBlockRequest
    {
        public string type;
        public SerializableVector3 position;
    }
}