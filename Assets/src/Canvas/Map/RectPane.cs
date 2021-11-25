using System;
using System.Collections.Generic;
using src.Model;
using src.Service;
using UnityEngine;
using UnityEngine.UI;

namespace src.Canvas.Map
{
    public class RectPane : MonoBehaviour
    {
        [SerializeField] private RectTransform playerPosIndicator;
        private readonly HashSet<GameObject> landIndicators = new HashSet<GameObject>();
        private readonly HashSet<GameObject> drawnLandIndicators = new HashSet<GameObject>();

        [SerializeField] public GameObject landPrefab;
        [SerializeField] public Button transferButton;
        [SerializeField] public Button toggleNftButton;

        public TransferHandler selectedLand;

        void Start()
        {
            var manager = GameManager.INSTANCE;
            if (manager.GetState() == GameManager.State.MAP) Init();

            manager.stateChange.AddListener(s =>
            {
                if (s == GameManager.State.MAP) Init();
                else DestroyRects();
            });

            transferButton.onClick.AddListener(DoTransfer);
            toggleNftButton.onClick.AddListener(DoToggleNft);
        }

        private void DoTransfer()
        {
            GameManager.INSTANCE.Transfer(selectedLand.land.id, selectedLand.land.isNft);
        }

        private void DoToggleNft()
        {
            GameManager.INSTANCE.SetNFT(selectedLand.land.id, !selectedLand.land.isNft);
        }

        private void Init()
        {
            VoxelService service = VoxelService.INSTANCE;
            if (!service.IsInitialized()) return;

            var playerPos = Player.INSTANCE.transform.position;
            playerPosIndicator.localPosition = new Vector3(playerPos.x, playerPos.z, 0);
            var transform = GetComponent<RectTransform>();
            transform.anchoredPosition = new Vector3(-playerPos.x, -playerPos.z, 0);

            foreach (var entry in service.GetOwnersLands())
            {
                bool owner = entry.Key.Equals(Settings.WalletId());
                foreach (var land in entry.Value)
                    Add(land.x1, land.x2, land.y1, land.y2,
                        owner ? land.isNft ? new Color(250, 237, 95) : Color.green : Color.gray, land, entry.Key);
            }
        }

        private void DestroyRects()
        {
            foreach (var lo in landIndicators)
                DestroyImmediate(lo);
            landIndicators.Clear();
            drawnLandIndicators.Clear();
        }


        internal void setSelected(TransferHandler transferHandler)
        {
            if (selectedLand != null && transferHandler != selectedLand)
                selectedLand.setSelected(false, true);
            selectedLand = transferHandler;
            transferButton.gameObject.SetActive(selectedLand != null &&
                                                selectedLand.walletId.Equals(Settings.WalletId()));
            toggleNftButton.gameObject.SetActive(selectedLand != null &&
                                                 selectedLand.walletId.Equals(Settings.WalletId()));
            if (toggleNftButton.gameObject.activeSelf)
            {
                toggleNftButton.gameObject.GetComponentInChildren<Text>().text =
                    selectedLand.land.isNft ? "Remove NFT" : "Create NFT";
            }
        }

        private GameObject Add(long x1, long x2, long y1, long y2, Color color, Land land, string walletId)
        {
            var landObject = Instantiate(landPrefab);
            var transform = landObject.GetComponent<RectTransform>();
            TransferHandler transferHandler = landObject.GetComponent<TransferHandler>();
            transferHandler.land = land;
            transferHandler.walletId = walletId;
            transferHandler.rectPane = this;

            transform.SetParent(this.transform);
            transform.SetAsFirstSibling();
            transform.pivot = new Vector2(0, 0);
            transform.localPosition = new Vector3(x1, y1, 0);
            transform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y2 - y1);
            transform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, x2 - x1);
            landObject.GetComponent<Image>().color = color;
            landIndicators.Add(landObject);
            return landObject;
        }

        internal void Delete(GameObject drawingObject)
        {
            DestroyImmediate(drawingObject);
            drawnLandIndicators.Remove(drawingObject);
            landIndicators.Remove(drawingObject);
        }

        internal GameObject DrawAt(long x, long y)
        {
            GameObject obj = Add(x, x, y, y, Color.blue, null, Settings.WalletId());
            drawnLandIndicators.Add(obj);
            return obj;
        }

        internal Rect ResolveCollisions(GameObject landIndicator, int x1, int x2, int y1, int y2, int dx1, int dx2,
            int dy1, int dy2)
        {
            if (dx2 != 0 || dy2 != 0)
            {
                if (dx2 > 0)
                {
                    var maxX2 = ForEachIndicator(x2 + dx2, (ox1, ox2, oy1, oy2, maxX2) =>
                    {
                        if (ox2 > x1 && (y1 > oy1 && y1 < oy2 || y2 > oy1 && y2 < oy2
                                                              || oy1 > y1 && oy1 < y2 || oy2 > y1 && oy2 < y2))
                            return Math.Min(maxX2, ox1);
                        return maxX2;
                    }, landIndicator);

                    x2 = Math.Max(maxX2, x2);
                }
                else x2 = Math.Max(x1, x2 + dx2);

                if (dy2 > 0)
                {
                    var maxY2 = ForEachIndicator(y2 + dy2, (ox1, ox2, oy1, oy2, maxY2) =>
                    {
                        if (oy2 > y1 && (x1 > ox1 && x1 < ox2 || x2 > ox1 && x2 < ox2
                                                              || ox1 > x1 && ox1 < x2 || ox2 > x1 && ox2 < x2))
                            return Math.Min(maxY2, oy1);
                        return maxY2;
                    }, landIndicator);

                    y2 = Math.Max(maxY2, y2);
                }
                else y2 = Math.Max(y1, y2 + dy2);
            }

            if (dy1 != 0 || dx1 != 0)
            {
                if (dx1 < 0)
                {
                    var minX1 = ForEachIndicator(x1 + dx1, (ox1, ox2, oy1, oy2, minX1) =>
                    {
                        if (ox1 < x2 && (y1 > oy1 && y1 < oy2 || y2 > oy1 && y2 < oy2
                                                              || oy1 > y1 && oy1 < y2 || oy2 > y1 && oy2 < y2))
                            return Math.Max(minX1, ox2);
                        return minX1;
                    }, landIndicator);

                    x1 = Math.Min(minX1, x1);
                }
                else x1 = Math.Min(x2, x1 + dx1);

                if (dy1 < 0)
                {
                    var minY1 = ForEachIndicator(y1 + dy1, (ox1, ox2, oy1, oy2, minY1) =>
                    {
                        if (oy1 < y2 && (x1 > ox1 && x1 < ox2 || x2 > ox1 && x2 < ox2
                                                              || ox1 > x1 && ox1 < x2 || ox2 > x1 && ox2 < x2))
                            return Math.Max(minY1, oy2);
                        return minY1;
                    }, landIndicator);

                    y1 = Math.Min(minY1, y1);
                }
                else y1 = Math.Min(y2, y1 + dy1);
            }

            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }


        /*
         * function is (indicatorX1, indicatorX2, indicatorY1, indicatorY2, current) => next
         */
        private int ForEachIndicator(int seed, Func<int, int, int, int, int, int> function, GameObject ignore)
        {
            var current = seed;
            foreach (var li in landIndicators)
            {
                if (li != ignore)
                {
                    var transform = li.GetComponent<RectTransform>();
                    var or = transform.rect;
                    var olp = transform.localPosition;
                    int x1 = TargetLines.RoundDown((int) olp.x);
                    int x2 = TargetLines.RoundUp(x1 + (int) or.width);
                    int y1 = TargetLines.RoundDown((int) olp.y);
                    int y2 = TargetLines.RoundUp(y1 + (int) or.height);

                    current = function.Invoke(x1, x2, y1, y2, current);
                }
            }

            return current;
        }

        internal List<Land> GetDrawn()
        {
            var drawn = new List<Land>();
            foreach (var indicator in drawnLandIndicators)
            {
                var transform = indicator.GetComponent<RectTransform>();
                var r = transform.rect;
                var land = new Land();
                land.x1 = (long) transform.localPosition.x;
                land.y1 = (long) transform.localPosition.y;
                land.x2 = land.x1 + (long) r.width;
                land.y2 = land.y1 + (long) r.height;
                drawn.Add(land);
            }

            return drawn;
        }

        internal bool HasDrawn()
        {
            return drawnLandIndicators.Count != 0;
        }
    }
}