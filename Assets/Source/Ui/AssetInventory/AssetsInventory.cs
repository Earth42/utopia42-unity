using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Source.MetaBlocks;
using Source.Model;
using Source.Ui.AssetInventory.Assets;
using Source.Ui.AssetInventory.Models;
using Source.Ui.AssetInventory.Slots;
using Source.Ui.Menu;
using Source.Ui.TabPane;
using Source.Ui.Utils;
using Source.Utils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Source.Ui.AssetInventory
{
    public class AssetsInventory : MonoBehaviour
    {
        private static AssetsInventory instance;
        private static readonly string HANDY_SLOTS_KEY = "HANDY_SLOTS";

        internal readonly AssetsRestClient restClient = new();
        private VisualElement root;
        private VisualElement inventory;
        private VisualElement handyPanel;
        private VisualElement handyPanelRoot;
        private Button openCloseInvButton;
        private VisualElement hammerModeArea;
        private ScrollView handyBar;

        private Sprite closeIcon;
        private Sprite openIcon;
        private Sprite addToFavoriteIcon;
        private Sprite removeFromFavoriteIcon;
        private Sprite hammerIcon;

        private List<InventorySlotWrapper> handyBarSlots = new();

        private InventorySlot selectedSlot;
        public readonly UnityEvent<SlotInfo> selectedSlotChanged = new();

        [SerializeField] private ColorSlotPicker colorSlotPicker;
        private Foldout colorBlocksFoldout;
        private List<FavoriteItem> favoriteItems;
        private TabPane.TabPane tabPane;
        private VisualElement inventoryContainer;
        private bool firstTime = true;
        private int selectedHandySlotIndex = -1;
        private UnityAction<bool> focusListener;
        private SimpleInventorySlot hammerSlot;

        void Start()
        {
            openIcon = Resources.Load<Sprite>("Icons/openPane");
            closeIcon = Resources.Load<Sprite>("Icons/closePane");

            addToFavoriteIcon = Resources.Load<Sprite>("Icons/whiteHeart");
            removeFromFavoriteIcon = Resources.Load<Sprite>("Icons/redHeart");

            hammerIcon = Resources.Load<Sprite>("Icons/hammer");

            GameManager.INSTANCE.stateChange.AddListener(_ => UpdateVisibility());
            Player.INSTANCE.viewModeChanged.AddListener(_ => UpdateVisibility());
            UpdateVisibility();

            Player.INSTANCE.InitOnSelectedAssetChanged(); // TODO ?
        }

        void OnEnable()
        {
            if (firstTime)
            {
                firstTime = false;
                instance = this;
                PlayerPrefs.SetString(HANDY_SLOTS_KEY, "[]");
            }

            root = GetComponent<UIDocument>().rootVisualElement;
            inventory = root.Q<VisualElement>("inventory");
            inventoryContainer = root.Q<VisualElement>("inventoryContainer");

            var tabConfigurations = new List<TabConfiguration>
            {
                new("Assets", new AssetsTab(this, inventory)),
                new("Blocks", "Ui/AssetInventory/BlocksTab", e => SetupBlocksTab()),
                new("Favorites", "Ui/AssetInventory/FavoritesTab", e => SetupFavoritesTab())
            };
            tabPane = new TabPane.TabPane(tabConfigurations);
            var s = tabPane.style;
            s.paddingBottom = s.paddingTop = 6;
            s.flexGrow = 1;
            s.width = new StyleLength(new Length(100, LengthUnit.Percent));
            inventory.Add(tabPane);

            handyPanelRoot = root.Q<VisualElement>("handyPanelRoot");
            handyPanel = root.Q<VisualElement>("handyPanel");
            handyBar = handyPanel.Q<ScrollView>("handyBar");
            Scrolls.IncreaseScrollSpeed(handyBar, 600);
            openCloseInvButton = handyPanel.Q<Button>("openCloseInvButton");
            openCloseInvButton.clickable.clicked += ToggleInventory;

            hammerModeArea = handyPanel.Q<VisualElement>("hammerModeArea");
            hammerSlot = new SimpleInventorySlot();
            hammerSlot.HideSlotBackground();
            hammerSlot.SetBackground(hammerIcon);
            hammerSlot.SetSlotInfo(new SlotInfo());
            hammerSlot.SetSize(80, 10);
            hammerModeArea.Add(hammerSlot.VisualElement());

            handyBarSlots.Clear();
            handyBar.Clear();
            var savedHandySlots = GetSavedHandySlots();
            savedHandySlots.Reverse();
            foreach (var savedHandySlot in savedHandySlots)
                AddToHandyPanel(savedHandySlot);

            var locked = MouseLook.INSTANCE.cursorLocked;
            root.focusable = !locked;
            root.SetEnabled(!locked);
            if (focusListener != null)
                MouseLook.INSTANCE.cursorLockedStateChanged.RemoveListener(focusListener);
            focusListener = locked =>
            {
                root.focusable = !locked;
                root.SetEnabled(!locked);
                if (inventoryContainer.style.visibility == Visibility.Visible)
                    ToggleInventory();
            };
            MouseLook.INSTANCE.cursorLockedStateChanged.AddListener(focusListener);
        }

        private void UpdateVisibility()
        {
            var active = GameManager.INSTANCE.GetState() == GameManager.State.PLAYING
                         && Player.INSTANCE.GetViewMode() == Player.ViewMode.FIRST_PERSON
                         && !AuthService.IsGuest()
                ; // && Can Edit Land 
            gameObject.SetActive(active);
            inventoryContainer.style.visibility = Visibility.Visible; // is null at start and can't be checked !
            ToggleInventory();
            if (active)
                LoadFavoriteItems(() =>
                {
                    if (handyBarSlots.Count == 0)
                    {
                        foreach (var favoriteItem in favoriteItems)
                            AddToHandyPanel(favoriteItem.ToSlotInfo());
                    }

                    SelectSlot(null);
                }, () =>
                {
                    SelectSlot(null);
                    // FIXME: what to do on error?
                });
        }

        private void SetupBlocksTab()
        {
            var scrollView = tabPane.GetTabBody().Q<ScrollView>("blockPacks");
            scrollView.Clear();
            Scrolls.IncreaseScrollSpeed(scrollView, 600);
            scrollView.mode = ScrollViewMode.Vertical;
            scrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;

            var regularBlocksFoldout = CreateBlocksPackFoldout("Regular Blocks",
                Blocks.GetBlockTypes().Where(blockType => blockType is not MetaBlockType && blockType.name != "air")
                    .ToList());

            var metaBlocksFoldout = CreateBlocksPackFoldout("Meta Blocks",
                Blocks.GetBlockTypes().Where(blockType => blockType is MetaBlockType).ToList());

            colorSlotPicker.SetOnColorCreated(color =>
            {
                ColorBlocks.SaveBlockColor(color);
                UpdateUserColorBlocks();
            });
            colorSlotPicker.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);

            scrollView.Add(regularBlocksFoldout);
            scrollView.Add(metaBlocksFoldout);

            colorBlocksFoldout = CreateColorBlocksFoldout();
            UpdateUserColorBlocks();
            scrollView.Add(colorBlocksFoldout);
        }

        private void SetupFavoritesTab()
        {
            LoadFavoriteItems(() =>
            {
                var scrollView = tabPane.GetTabBody().Q<ScrollView>("favorites");
                var container = new VisualElement();
                for (int i = 0; i < favoriteItems.Count; i++)
                {
                    var favoriteItem = favoriteItems[i];
                    var slot = new FavoriteItemInventorySlot(favoriteItem);
                    GridUtils.SetChildPosition(slot.VisualElement(), 80, 80, i, 3);
                    container.Add(slot.VisualElement());
                }

                GridUtils.SetContainerSize(container, favoriteItems.Count, 90, 3);
                scrollView.Add(container);
            }, () =>
            {
                //TODO: show error snack
            }, true);
        }

        private void Update()
        {
            if (selectedSlot != null && (Input.GetButtonDown("Clear slot selection") || Input.GetMouseButtonDown(1)) &&
                !Player.INSTANCE.SelectionActiveBeforeAtFrameBeginning)
                SelectSlot(null);

            if (handyBar.childCount == 0 || !MouseLook.INSTANCE.cursorLocked)
                return;

            var mouseDelta = Input.mouseScrollDelta.y;
            var inc = Input.GetButtonDown("Change Block") || mouseDelta <= -0.1;
            var dec = Input.GetButtonDown("Change Block") &&
                      (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                      || mouseDelta >= 0.1;
            if (!dec && !inc)
                return;
            if (dec)
            {
                if (selectedHandySlotIndex is -1 or 0)
                    selectedHandySlotIndex = handyBar.childCount - 1;
                else
                    selectedHandySlotIndex--;
            }
            else
            {
                if (selectedHandySlotIndex == handyBar.childCount - 1)
                    selectedHandySlotIndex = 0;
                else
                    selectedHandySlotIndex++;
            }

            handyBar.ScrollTo(handyBarSlots[selectedHandySlotIndex].VisualElement());
            SelectSlot(handyBarSlots[selectedHandySlotIndex], false);
        }

        private void ToggleInventory()
        {
            var isVisible = inventoryContainer.style.visibility == Visibility.Visible;
            inventoryContainer.style.visibility = isVisible ? Visibility.Hidden : Visibility.Visible;
            handyPanel.style.right = isVisible ? 5 : 362;
            var background = new StyleBackground
            {
                value = Background.FromSprite(isVisible ? openIcon : closeIcon)
            };
            openCloseInvButton.style.backgroundImage = background;
            if (!isVisible)
                OpenAssetsTab();
        }

        private void LoadFavoriteItems(Action onDone = null, Action onFail = null, bool forFavoritesTab = false)
        {
            var loading = LoadingLayer.LoadingLayer.Show(forFavoritesTab ? inventory : handyPanelRoot);
            StartCoroutine(restClient.GetAllFavoriteItems(new SearchCriteria(), favItems =>
            {
                favoriteItems = favItems;
                onDone?.Invoke();
                loading.Close();
            }, () =>
            {
                loading.Close();
                onFail?.Invoke();
            }, this));
        }

        private void UpdateUserColorBlocks()
        {
            if (colorBlocksFoldout.childCount == 2)
                colorBlocksFoldout.RemoveAt(1);
            var colorSlotsContainer = CreateUserColorBlocks();
            colorBlocksFoldout.contentContainer.Add(colorSlotsContainer);
            colorBlocksFoldout.contentContainer.style.height = colorSlotsContainer.style.height.value.value + 75;
        }

        private Foldout CreateColorBlocksFoldout()
        {
            var foldout = new PackFoldout<VisualElement>("Color Blocks", true);
            var colorBlockCreator = Utils.Utils.Create("Ui/AssetInventory/ColorBlockCreator");
            var colorPickerToggle = colorBlockCreator.Q<Button>();
            colorPickerToggle.style.height = 70;
            colorPickerToggle.clickable.clicked += () => colorSlotPicker.ToggleColorPicker();
            foldout.contentContainer.Add(colorPickerToggle);
            return foldout;
        }

        private VisualElement CreateUserColorBlocks()
        {
            var playerColorBlocks = ColorBlocks.GetPlayerColorBlocks();
            var size = playerColorBlocks.Count;
            var slotsContainer = new VisualElement();
            for (var i = 0; i < size; i++)
            {
                var slot = new ColorBlockInventorySlot();
                ColorUtility.TryParseHtmlString(playerColorBlocks[i], out var color);
                slot.SetSlotInfo(new SlotInfo(ColorBlocks.GetBlockTypeFromColor(color)));
                slot.SetSize(80);
                slot.SetGridPosition(i, 3);
                SetupFavoriteAction(slot);
                slotsContainer.Add(slot.VisualElement());
            }

            slotsContainer.style.height = 90 * (size / 3 + 1);
            return slotsContainer;
        }

        public void DeleteColorBlock(ColorBlockInventorySlot colorBlockInventorySlot)
        {
            ColorBlocks.RemoveBlockColorFromSaving(colorBlockInventorySlot.color);
            UpdateUserColorBlocks();
        }

        private Foldout CreateBlocksPackFoldout(string name, List<BlockType> blocks)
        {
            var foldout = new PackFoldout<VisualElement>(name, true);

            var size = blocks.Count;
            if (size <= 0) return foldout;
            for (var i = 0; i < size; i++)
            {
                var slot = new BlockInventorySlot();
                var slotInfo = new SlotInfo(blocks[i]);
                slot.SetSlotInfo(slotInfo);
                slot.SetSize(80);
                slot.SetGridPosition(i, 3);
                SetupFavoriteAction(slot);
                foldout.contentContainer.Add(slot.VisualElement());
            }

            GridUtils.SetContainerSize(foldout.contentContainer, size, 90, 3);
            return foldout;
        }

        private void OpenAssetsTab()
        {
            tabPane.OpenTab(0);
        }

        private void SetupFavoriteAction(BaseInventorySlot slot)
        {
            var slotInfo = slot.GetSlotInfo();
            var isFavorite = IsUserFavorite(slotInfo, out _);
            slot.ConfigRightAction(isFavorite ? "Remove from favorites" : "Add to favorites",
                isFavorite ? removeFromFavoriteIcon : addToFavoriteIcon,
                () =>
                {
                    var isFavorite = IsUserFavorite(slotInfo, out _);
                    if (isFavorite)
                        RemoveFromFavorites(slot);
                    else
                        AddToFavorites(slot);
                });
            slot.SetRightActionVisible(true);
        }

        private bool IsUserFavorite(SlotInfo slotInfo, out FavoriteItem favoriteItem)
        {
            foreach (var item in favoriteItems)
            {
                if (item.asset != null &&
                    slotInfo.asset != null &&
                    item.asset.id.Value == slotInfo.asset.id.Value)
                {
                    favoriteItem = item;
                    return true;
                }

                if (
                    item.blockId != null &&
                    slotInfo.block != null &&
                    item.blockId.HasValue && item.blockId.Value == slotInfo.block.id)
                {
                    favoriteItem = item;
                    return true;
                }
            }

            favoriteItem = null;
            return false;
        }

        public void AddToFavorites(BaseInventorySlot slot)
        {
            var slotInfo = slot.GetSlotInfo();
            var loading = LoadingLayer.LoadingLayer.Show(inventory);
            var favoriteItem = new FavoriteItem
            {
                asset = slotInfo.asset,
                blockId = slotInfo.block?.id
            };
            StartCoroutine(restClient.CreateFavoriteItem(favoriteItem, item =>
                {
                    loading.Close();
                    favoriteItems.Add(item);
                    SetupFavoriteAction(slot);
                },
                () =>
                {
                    loading.Close();
                    //TODO a toast?
                }));
        }

        public void RemoveFromFavorites(FavoriteItem favoriteItem, BaseInventorySlot slot, Action onDone = null)
        {
            var loading = LoadingLayer.LoadingLayer.Show(inventory);
            StartCoroutine(restClient.DeleteFavoriteItem(favoriteItem.id.Value,
                () =>
                {
                    loading.Close();
                    favoriteItems.Remove(favoriteItem);
                    SetupFavoriteAction(slot);
                    onDone?.Invoke();
                    //TODO a toast?
                }, () =>
                {
                    loading.Close();
                    //TODO a toast?
                }));
        }

        public void RemoveFromFavorites(BaseInventorySlot slot)
        {
            var slotInfo = slot.GetSlotInfo();
            if (IsUserFavorite(slotInfo, out var favoriteItem))
                RemoveFromFavorites(favoriteItem, slot);
            else
                throw new Exception("Trying to remove from favorites a slot that is not a favorite");
        }

        public void SelectSlot(InventorySlot slot, bool addToHandyPanel = true)
        {
            selectedSlot?.SetSelected(false);
            if (selectedSlot == slot || slot == null)
            {
                selectedSlot = null;
                selectedSlotChanged.Invoke(null);
                selectedHandySlotIndex = -1;
                return;
            }

            selectedSlot = slot;
            slot.SetSelected(true);
            var slotInfo = slot.GetSlotInfo();
            selectedSlotChanged.Invoke(slotInfo);
            if (!slotInfo.IsEmpty())
            {
                if (addToHandyPanel)
                    AddToHandyPanel(slotInfo);
                else // from handy bar itself
                {
                    for (var i = 0; i < handyBarSlots.Count; i++)
                    {
                        if (handyBarSlots[i] == slot)
                        {
                            selectedHandySlotIndex = i;
                            break;
                        }
                    }
                }
            }
            else
            {
                selectedHandySlotIndex = -1;
            }
        }

        public void SelectSlotInfo(SlotInfo slotInfo)
        {
            if (slotInfo == null)
            {
                SelectSlot(null);
            }
            else if (slotInfo.IsEmpty())
            {
                SelectSlot(hammerSlot);
            }
            else
            {
                foreach (var handyBarSlot in handyBarSlots)
                {
                    if (Equals(handyBarSlot.GetSlotInfo(), slotInfo))
                    {
                        SelectSlot(handyBarSlot);
                        break;
                    }
                }
            }
        }

        public void AddToHandyPanel(SlotInfo slotInfo)
        {
            foreach (var handyBarSlot in handyBarSlots)
            {
                if (handyBarSlot.GetSlotInfo().Equals(slotInfo))
                {
                    handyBarSlots.Remove(handyBarSlot);
                    handyBarSlots.Insert(0, handyBarSlot);
                    if (handyBar.Contains(handyBarSlot.VisualElement()))
                        handyBar.Remove(handyBarSlot.VisualElement());
                    else
                        handyBar.RemoveAt(handyBar.childCount - 1);
                    handyBar.Insert(0, handyBarSlot.VisualElement());
                    SelectSlot(handyBarSlot, false);
                    SaveHandySlots();
                    return;
                }
            }

            var slot = new HandyItemInventorySlot();
            slot.SetSize(70);
            slot.SetSlotInfo(slotInfo);
            if (handyBarSlots.Count == 15)
                handyBarSlots.RemoveAt(14);
            handyBarSlots.Insert(0, slot);
            handyBar.Insert(0, slot.VisualElement());
            if (handyBar.childCount > 10)
                handyBar.RemoveAt(handyBar.childCount - 1);
            SaveHandySlots();
            selectedHandySlotIndex = 0;
            SelectSlot(slot, false);
        }

        public void RemoveFromHandyPanel(InventorySlotWrapper slot)
        {
            handyBarSlots.Remove(slot);
            slot.VisualElement().RemoveFromHierarchy();
            while (handyBar.childCount < 10 && handyBarSlots.Count >= 10)
                handyBar.Add(handyBarSlots[handyBar.childCount].VisualElement());
            SaveHandySlots();
        }

        private void SaveHandySlots()
        {
            var items = handyBarSlots.Select(slot => SerializableSlotInfo.FromSlotInfo(slot.GetSlotInfo())).ToList();
            PlayerPrefs.SetString(HANDY_SLOTS_KEY, JsonConvert.SerializeObject(items));
        }

        private List<SlotInfo> GetSavedHandySlots()
        {
            return JsonConvert
                .DeserializeObject<List<SerializableSlotInfo>>(PlayerPrefs.GetString(HANDY_SLOTS_KEY, "[]"))
                .Select(serializedSlotInfo => serializedSlotInfo.ToSlotInfo()).ToList();
        }

        public SlotInfo GetSelectedSlot()
        {
            return selectedSlot.GetSlotInfo();
        }

        public static AssetsInventory INSTANCE => instance;

        public void ReloadTab()
        {
            tabPane.OpenTab(tabPane.GetCurrentTabIndex());
        }
    }
}