using System;
using Newtonsoft.Json;
using src;
using src.Model;
using src.Service;
using UnityEngine;

public partial class UtopiaApi : MonoBehaviour
{
    public Player player;

    public string PlaceBlock(String request)
    {
        var req = JsonConvert.DeserializeObject<PlaceBlockRequest>(request);
        Debug.Log(req);
        var placed = player.PutBlock(new Vector3(req.position.x, req.position.y, req.position.z),
            WorldService.INSTANCE.GetBlockType(req.type), true);
        Debug.Log(placed);
        return JsonConvert.SerializeObject(placed);
    }

    public string GetPlayerPosition()
    {
        var pos = Player.INSTANCE.transform.position;
        return JsonConvert.SerializeObject(new SerializableVector3(pos));
    }

    public string GetMarkers()
    {
        return JsonConvert.SerializeObject(WorldService.INSTANCE.GetMarkers());
    }

    public string GetPlayerLands(string walletId)
    {
        return JsonConvert.SerializeObject(WorldService.INSTANCE.GetLandsFor(walletId));
    }

    public string GetBlockTypes()
    {
        return JsonConvert.SerializeObject(WorldService.INSTANCE.GetNonMetaBlockTypes());
    }

    private class PlaceBlockRequest
    {
        public string type;
        public SerializableVector3 position;
    }
}